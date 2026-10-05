using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Helpdesk.Contracts.Auth;
using Helpdesk.Contracts.Catalog;
using Helpdesk.Contracts.Common;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Contracts.Reports;
using Helpdesk.Contracts.Tickets;
using Microsoft.AspNetCore.Components.Forms;

namespace Helpdesk.Web.Services;

/// <summary>Error devuelto por la API como ProblemDetails (RFC 9457).</summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode status, string message, string? code, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Status = status;
        Code = code;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public ApiException()
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ApiException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ApiException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public HttpStatusCode Status { get; }

    public string? Code { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>El ticket cambió mientras se editaba (If-Match desactualizado o conflicto de concurrencia).</summary>
    public bool IsStale => Status == HttpStatusCode.PreconditionFailed || Code == "concurrency_conflict";

    public string Friendly => Errors.Count > 0 ? string.Join(" ", Errors.SelectMany(e => e.Value)) : Message;
}

/// <summary>Cliente tipado de la API. Todas las rutas pasan por <see cref="AuthorizingHandler"/>.</summary>
public sealed class HelpdeskApi(HttpClient http)
{
    // ---- Tickets
    public Task<PagedResult<TicketListItemDto>> ListTicketsAsync(TicketQuery query) => GetAsync<PagedResult<TicketListItemDto>>("api/v1/tickets" + Query(query));

    public Task<TicketDetailDto> GetTicketAsync(Guid id) => GetAsync<TicketDetailDto>($"api/v1/tickets/{id}");

    public Task<TicketDetailDto> CreateTicketAsync(CreateTicketRequest request) => SendAsync<TicketDetailDto>(HttpMethod.Post, "api/v1/tickets", request);

    public Task<TicketDetailDto> AssignAsync(Guid id, Guid assigneeId, uint version) =>
        SendAsync<TicketDetailDto>(HttpMethod.Post, $"api/v1/tickets/{id}/assign", new AssignRequest { AssigneeId = assigneeId }, version);

    public Task<TicketDetailDto> TransferAsync(Guid id, TransferRequest request, uint version) =>
        SendAsync<TicketDetailDto>(HttpMethod.Post, $"api/v1/tickets/{id}/transfer", request, version);

    public Task<TicketDetailDto> TransitionAsync(Guid id, TransitionRequest request, uint version) =>
        SendAsync<TicketDetailDto>(HttpMethod.Post, $"api/v1/tickets/{id}/transitions", request, version);

    public Task<TicketDetailDto> ClassifyAsync(Guid id, ClassifyRequest request, uint version) =>
        SendAsync<TicketDetailDto>(HttpMethod.Put, $"api/v1/tickets/{id}/classification", request, version);

    public Task<CommentDto> AddCommentAsync(Guid id, CreateCommentRequest request) => SendAsync<CommentDto>(HttpMethod.Post, $"api/v1/tickets/{id}/comments", request);

    public Task<TicketDetailDto> SubmitSurveyAsync(Guid id, SurveyRequest request) => SendAsync<TicketDetailDto>(HttpMethod.Post, $"api/v1/tickets/{id}/survey", request);

    public async Task<AttachmentDto> UploadAsync(Guid id, IBrowserFile file, bool isInternal)
    {
        using var form = new MultipartFormDataContent();
        using var stream = file.OpenReadStream(Contracts.Validation.AttachmentClientRules.MaxBytes + 1);
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType);
        form.Add(content, "file", file.Name);
        form.Add(new StringContent(isInternal ? "true" : "false"), "isInternal");
        using var response = await http.PostAsync(new Uri($"api/v1/tickets/{id}/attachments", UriKind.Relative), form);
        return await ReadAsync<AttachmentDto>(response);
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> DownloadAsync(string url, string fallbackName)
    {
        using var response = await http.GetAsync(new Uri(url, UriKind.Relative));
        await EnsureSuccessAsync(response);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? fallbackName;
        return (bytes, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", name);
    }

    // ---- Sugerencias
    public Task<List<SuggestionDto>> SuggestionsAsync(Guid ticketId) => GetAsync<List<SuggestionDto>>($"api/v1/tickets/{ticketId}/suggestions");

    public Task RequestSuggestionAsync(Guid ticketId) => SendAsync(HttpMethod.Post, $"api/v1/tickets/{ticketId}/suggestions/request");

    public Task<SuggestionDto> AcceptSuggestionAsync(Guid id) => SendAsync<SuggestionDto>(HttpMethod.Post, $"api/v1/suggestions/{id}/accept");

    public Task<SuggestionDto> CorrectSuggestionAsync(Guid id, CorrectSuggestionRequest request) => SendAsync<SuggestionDto>(HttpMethod.Post, $"api/v1/suggestions/{id}/correct", request);

    public Task<SuggestionDto> DismissSuggestionAsync(Guid id) => SendAsync<SuggestionDto>(HttpMethod.Post, $"api/v1/suggestions/{id}/dismiss");

    // ---- Catálogos
    public Task<List<CategoryDto>> CategoriesAsync() => GetAsync<List<CategoryDto>>("api/v1/categories");

    public Task<List<TeamDto>> TeamsAsync() => GetAsync<List<TeamDto>>("api/v1/teams");

    public Task<List<AgentDto>> AgentsAsync(Guid? teamId = null) => GetAsync<List<AgentDto>>("api/v1/agents" + (teamId is { } t ? $"?teamId={t}" : string.Empty));

    public Task<List<PriorityMatrixCellDto>> PriorityMatrixAsync() => GetAsync<List<PriorityMatrixCellDto>>("api/v1/priority-matrix");

    public Task<TeamDto> SetTeamMembersAsync(Guid teamId, SetTeamMembersRequest request) => SendAsync<TeamDto>(HttpMethod.Put, $"api/v1/teams/{teamId}/members", request);

    public Task<PagedResult<CustomerDto>> CustomersAsync(string? search, int page = 1, int pageSize = 50) =>
        GetAsync<PagedResult<CustomerDto>>($"api/v1/customers?page={page}&pageSize={pageSize}" + (string.IsNullOrWhiteSpace(search) ? string.Empty : "&search=" + Uri.EscapeDataString(search)));

    public Task<CustomerDto> CreateCustomerAsync(SaveCustomerRequest request) => SendAsync<CustomerDto>(HttpMethod.Post, "api/v1/customers", request);

    public Task<CustomerDto> AnonymizeCustomerAsync(Guid id) => SendAsync<CustomerDto>(HttpMethod.Post, $"api/v1/customers/{id}/anonymize");

    public Task<List<UserDto>> UsersAsync() => GetAsync<List<UserDto>>("api/v1/users");

    public Task<UserDto> CreateUserAsync(CreateUserRequest request) => SendAsync<UserDto>(HttpMethod.Post, "api/v1/users", request);

    public Task SetUserStatusAsync(Guid id, bool isActive) => SendAsync(HttpMethod.Put, $"api/v1/users/{id}/status", new SetUserStatusRequest { IsActive = isActive });

    // ---- SLA
    public Task<List<SlaPolicyDto>> SlaPoliciesAsync() => GetAsync<List<SlaPolicyDto>>("api/v1/sla-policies");

    public Task<SlaPolicyDto> UpdateSlaPolicyAsync(Guid id, UpdateSlaPolicyRequest request) => SendAsync<SlaPolicyDto>(HttpMethod.Put, $"api/v1/sla-policies/{id}", request);

    public Task<List<CalendarDto>> CalendarsAsync() => GetAsync<List<CalendarDto>>("api/v1/calendars");

    public Task<HolidayDto> AddHolidayAsync(Guid calendarId, CreateHolidayRequest request) => SendAsync<HolidayDto>(HttpMethod.Post, $"api/v1/calendars/{calendarId}/holidays", request);

    public Task RemoveHolidayAsync(Guid calendarId, Guid holidayId) => SendAsync(HttpMethod.Delete, $"api/v1/calendars/{calendarId}/holidays/{holidayId}");

    // ---- Notificaciones
    public Task<List<NotificationDto>> NotificationsAsync(bool unreadOnly = false) => GetAsync<List<NotificationDto>>($"api/v1/notifications?unreadOnly={unreadOnly.ToString().ToLowerInvariant()}");

    public Task<UnreadCountDto> UnreadCountAsync() => GetAsync<UnreadCountDto>("api/v1/notifications/unread-count");

    public Task MarkReadAsync(Guid id) => SendAsync(HttpMethod.Post, $"api/v1/notifications/{id}/read");

    public Task MarkAllReadAsync() => SendAsync(HttpMethod.Post, "api/v1/notifications/read-all");

    // ---- Reportes
    public Task<ReportOverviewDto> OverviewAsync(ReportQuery query) => GetAsync<ReportOverviewDto>("api/v1/reports/overview" + Query(query));

    public Task<List<AgentWorkloadRow>> AgentWorkloadAsync(ReportQuery query) => GetAsync<List<AgentWorkloadRow>>("api/v1/reports/agents" + Query(query));

    public static string ReportCsvUrl(ReportQuery query) => "api/v1/reports/tickets.csv" + Query(query);

    // ---- Sesión
    public Task<CurrentUserDto> AcceptPrivacyAsync() =>
        SendAsync<CurrentUserDto>(HttpMethod.Post, "api/v1/auth/privacy-consent", new PrivacyConsentRequest { Version = PrivacyNotice.CurrentVersion });

    internal static string Query(TicketQuery q)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}", $"sort={Snake(q.Sort)}" };
        AddIf(parts, "status", q.Status is { } s ? Snake(s) : null);
        AddIf(parts, "priority", q.Priority is { } p ? Snake(p) : null);
        AddIf(parts, "sla", q.Sla is { } sla ? Snake(sla) : null);
        AddIf(parts, "teamId", q.TeamId?.ToString());
        AddIf(parts, "assigneeId", q.AssigneeId?.ToString());
        AddIf(parts, "customerId", q.CustomerId?.ToString());
        AddIf(parts, "search", string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim());
        if (q.OpenOnly)
        {
            parts.Add("openOnly=true");
        }

        if (q.AssignedToMe)
        {
            parts.Add("assignedToMe=true");
        }

        return "?" + string.Join('&', parts);
    }

    internal static string Query(ReportQuery q)
    {
        var parts = new List<string>();
        AddIf(parts, "from", q.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddIf(parts, "to", q.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddIf(parts, "teamId", q.TeamId?.ToString());
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private static void AddIf(List<string> parts, string name, string? value)
    {
        if (value is not null)
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string Snake<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await http.GetAsync(new Uri(url, UriKind.Relative));
        return await ReadAsync<T>(response);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null, uint? version = null)
    {
        using var request = Build(method, url, body, version);
        using var response = await http.SendAsync(request);
        return await ReadAsync<T>(response);
    }

    private async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var request = Build(method, url, body, null);
        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private static HttpRequestMessage Build(HttpMethod method, string url, object? body, uint? version)
    {
        var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonDefaults.Options);
        }

        if (version is { } v)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{v.ToString(CultureInfo.InvariantCulture)}\"");
        }

        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Tu sesión expiró. Vuelve a iniciar sesión.",
            HttpStatusCode.Forbidden => "No tienes permiso para esta acción.",
            HttpStatusCode.NotFound => "No se encontró el recurso.",
            HttpStatusCode.TooManyRequests => "Demasiadas solicitudes. Espera un momento.",
            HttpStatusCode.PreconditionFailed => "Otro usuario modificó el ticket. Se recargó con la última versión.",
            _ => "No se pudo completar la operación.",
        };
        Dictionary<string, string[]>? errors = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String && response.StatusCode is not HttpStatusCode.Unauthorized)
                {
                    message = detail.GetString() ?? message;
                }

                code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
                if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                {
                    errors = e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray());
                }
            }
        }
        catch (JsonException)
        {
            // Respuesta sin ProblemDetails: se usa el mensaje genérico.
        }

        throw new ApiException(response.StatusCode, message, code, errors);
    }
}
