using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Helpdesk.Application.Abstractions;
using Helpdesk.Domain.Tickets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Helpdesk.Infrastructure.Nlp;

/// <summary>
/// Conexión con nlp-tickets-es (P27). La URL y las claves vienen solo de variables de entorno; si no hay URL,
/// la integración queda desactivada y el helpdesk funciona igual.
/// </summary>
public sealed class NlpOptions
{
    public const string SectionName = "Nlp";

    public string? BaseUrl { get; set; }

    /// <summary>Clave con rol <c>client</c> de P27 (header X-API-Key).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Clave con rol <c>reviewer</c>: opcional, permite enviar las correcciones como retroalimentación.</summary>
    public string? ReviewerApiKey { get; set; }

    /// <summary>Tiempo máximo por llamada; alto porque Render free tarda ~1 min en despertar.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Cliente HTTP tipado con resiliencia (timeout, circuit breaker). Trata la respuesta como no confiable
/// (OWASP API10): valida códigos y puntajes y limita el tamaño de la respuesta.
/// </summary>
internal sealed partial class NlpTicketsClient(HttpClient http, IOptions<NlpOptions> options, ILogger<NlpTicketsClient> logger) : INlpClassifier
{
    public const string Provider = "nlp-tickets-es";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsEnabled => options.Value.IsEnabled;

    public bool CanSendFeedback => IsEnabled && !string.IsNullOrWhiteSpace(options.Value.ReviewerApiKey);

    public async Task<NlpClassification?> ClassifyAsync(string text, TicketChannel channel, string externalRef, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/classify")
        {
            Content = JsonContent.Create(new ClassifyRequest(text, MapChannel(channel), externalRef, Persist: true, Explain: false), options: Json),
        };
        request.Headers.Add("X-API-Key", options.Value.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedStatus(logger, (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<ClassifyResponse>(Json, cancellationToken);
            return body is null ? null : Map(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException or TaskCanceledException or JsonException or InvalidDataException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogUnavailable(logger, ex);
            return null;
        }
    }

    public async Task SendFeedbackAsync(Guid predictionId, string? categoryCode, string? teamCode, Urgency? urgency, CancellationToken cancellationToken)
    {
        if (!CanSendFeedback)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/feedback")
        {
            Content = JsonContent.Create(new FeedbackRequest(predictionId, categoryCode, teamCode, urgency is { } u ? MapUrgency(u) : null, "Corrección desde helpdesk-net"), options: Json),
        };
        request.Headers.Add("X-API-Key", options.Value.ReviewerApiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    internal static NlpClassification Map(ClassifyResponse body)
    {
        var suggestion = new SuggestedClassification(
            SafeCode(body.Category?.Code),
            SafeCode(body.Team?.Code),
            ParseUrgency(body.Urgency?.Code),
            SafeScore(body.Category?.Score),
            SafeScore(body.Team?.Score),
            body.Decision is "auto_route" or "human_review" ? body.Decision : null,
            [.. (body.ReviewReasons ?? []).Select(SafeCode).OfType<string>().Take(5)]);
        var version = body.Model?.Version is { Length: > 0 and <= 60 } v ? v : "desconocida";
        return new NlpClassification(Provider, version, body.PredictionId, suggestion);
    }

    internal static string MapChannel(TicketChannel channel) => channel switch
    {
        TicketChannel.Email => "email",
        TicketChannel.Phone => "phone",
        TicketChannel.Chat => "chat",
        TicketChannel.WalkIn => "branch",
        _ => "web",
    };

    internal static Urgency? ParseUrgency(string? code) => code switch
    {
        "high" => Urgency.High,
        "medium" => Urgency.Medium,
        "low" => Urgency.Low,
        _ => null,
    };

    private static string MapUrgency(Urgency urgency) => urgency switch
    {
        Urgency.High => "high",
        Urgency.Medium => "medium",
        _ => "low",
    };

    private static string? SafeCode(string? code) => code is not null && CodePattern().IsMatch(code) ? code : null;

    private static decimal? SafeScore(double? score) => score is >= 0 and <= 1 ? Math.Round((decimal)score.Value, 3) : null;

    [GeneratedRegex("^[a-z0-9_]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "El clasificador respondió con estado {StatusCode}")]
    private static partial void LogUnexpectedStatus(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El clasificador no está disponible; el ticket queda sin sugerencia")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    internal sealed record ClassifyRequest(string Text, string Channel, string ExternalRef, bool Persist, bool Explain);

    internal sealed record FeedbackRequest(Guid PredictionId, string? CorrectedCategory, string? CorrectedTeam, string? CorrectedUrgency, string Comment);

    internal sealed record LabelScore(string? Code, string? Name, double? Score);

    internal sealed record ModelRef(string? Name, string? Version);

    internal sealed record ClassifyResponse(
        Guid? TicketId,
        Guid? PredictionId,
        ModelRef? Model,
        string? Decision,
        IReadOnlyList<string>? ReviewReasons,
        LabelScore? Category,
        LabelScore? Team,
        LabelScore? Urgency);
}
