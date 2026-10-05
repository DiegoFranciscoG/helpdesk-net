using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Tickets;

public enum SuggestionStatus
{
    Pending,
    Accepted,
    Corrected,
    Dismissed,
}

/// <summary>De dónde vino la sugerencia: la API llamó a P27 o un cliente de integración la empujó con JWT.</summary>
public enum SuggestionSource
{
    NlpApi,
    Integration,
}

/// <summary>Valores que propone el clasificador.</summary>
public sealed record SuggestedClassification(
    string? CategoryCode,
    string? TeamCode,
    Urgency? Urgency,
    decimal? CategoryConfidence,
    decimal? TeamConfidence,
    string? Decision,
    IReadOnlyList<string> ReviewReasons);

/// <summary>
/// Sugerencia automática de categoría, equipo y urgencia. Nunca se aplica sola: un agente la acepta,
/// la corrige o la descarta (LOPDP art. 20, decisión con intervención humana).
/// </summary>
public sealed class TicketSuggestion : Entity
{
    public const int CodeMaxLength = 40;

    private TicketSuggestion()
    {
        Provider = string.Empty;
        ModelVersion = string.Empty;
        ReviewReasons = [];
    }

    public TicketSuggestion(
        Guid ticketId,
        SuggestionSource source,
        string provider,
        string modelVersion,
        Guid? externalPredictionId,
        SuggestedClassification suggestion,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ValidateConfidence(suggestion.CategoryConfidence);
        ValidateConfidence(suggestion.TeamConfidence);

        TicketId = ticketId;
        Source = source;
        Provider = provider;
        ModelVersion = modelVersion;
        ExternalPredictionId = externalPredictionId;
        SuggestedCategoryCode = Limit(suggestion.CategoryCode);
        SuggestedTeamCode = Limit(suggestion.TeamCode);
        SuggestedUrgency = suggestion.Urgency;
        CategoryConfidence = suggestion.CategoryConfidence;
        TeamConfidence = suggestion.TeamConfidence;
        Decision = suggestion.Decision;
        ReviewReasons = [.. suggestion.ReviewReasons];
        Status = SuggestionStatus.Pending;
        CreatedAt = createdAt;
    }

    public Guid TicketId { get; private set; }

    public SuggestionSource Source { get; private set; }

    public string Provider { get; private set; }

    public string ModelVersion { get; private set; }

    public Guid? ExternalPredictionId { get; private set; }

    public string? SuggestedCategoryCode { get; private set; }

    public string? SuggestedTeamCode { get; private set; }

    public Urgency? SuggestedUrgency { get; private set; }

    public decimal? CategoryConfidence { get; private set; }

    public decimal? TeamConfidence { get; private set; }

    public string? Decision { get; private set; }

    public string[] ReviewReasons { get; private set; }

    public SuggestionStatus Status { get; private set; }

    public Guid? FinalCategoryId { get; private set; }

    public Guid? FinalTeamId { get; private set; }

    public Urgency? FinalUrgency { get; private set; }

    public Guid? ReviewedById { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Accept(Guid reviewerId, Guid? categoryId, Guid? teamId, Urgency? urgency, DateTimeOffset now) =>
        Review(SuggestionStatus.Accepted, reviewerId, categoryId, teamId, urgency, now);

    public void Correct(Guid reviewerId, Guid? categoryId, Guid? teamId, Urgency? urgency, DateTimeOffset now) =>
        Review(SuggestionStatus.Corrected, reviewerId, categoryId, teamId, urgency, now);

    public void Dismiss(Guid reviewerId, DateTimeOffset now) =>
        Review(SuggestionStatus.Dismissed, reviewerId, null, null, null, now);

    private void Review(SuggestionStatus status, Guid reviewerId, Guid? categoryId, Guid? teamId, Urgency? urgency, DateTimeOffset now)
    {
        if (Status != SuggestionStatus.Pending)
        {
            throw new DomainException("suggestion_reviewed", "La sugerencia ya fue revisada.");
        }

        Status = status;
        ReviewedById = reviewerId;
        ReviewedAt = now;
        FinalCategoryId = categoryId;
        FinalTeamId = teamId;
        FinalUrgency = urgency;
    }

    private static void ValidateConfidence(decimal? value)
    {
        if (value is < 0m or > 1m)
        {
            throw new DomainException("invalid_confidence", "La confianza debe estar entre 0 y 1.");
        }
    }

    private static string? Limit(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim()[..Math.Min(code.Trim().Length, CodeMaxLength)];
}
