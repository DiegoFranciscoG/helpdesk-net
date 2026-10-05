using System.Text;
using Helpdesk.Api.Infrastructure;
using Helpdesk.Api.Security;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Attachments;
using Helpdesk.Application.Catalog;
using Helpdesk.Application.Notifications;
using Helpdesk.Application.Reports;
using Helpdesk.Application.Suggestions;
using Helpdesk.Contracts.Catalog;
using Helpdesk.Contracts.Common;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Contracts.Reports;
using Helpdesk.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helpdesk.Api.Controllers;

/// <summary>Descarga de adjuntos, siempre a través de la autorización del ticket.</summary>
[Route("api/v1/attachments")]
[Authorize(Policy = Policies.User)]
public sealed class AttachmentsController(AttachmentService attachments) : ApiControllerBase
{
    /// <summary>Descarga como adjunto (nunca se muestra en línea) con CSP sandbox y nosniff.</summary>
    [HttpGet("{id:guid}/content")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var content = await attachments.DownloadAsync(id, cancellationToken);
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(content.Content, content.ContentType, content.FileName);
    }
}

/// <summary>Revisión humana de sugerencias del clasificador (aceptar, corregir o descartar).</summary>
[Route("api/v1/suggestions")]
[Authorize(Policy = Policies.Staff)]
public sealed class SuggestionsController(SuggestionService suggestions) : ApiControllerBase
{
    [HttpPost("{id:guid}/accept")]
    public Task<SuggestionDto> Accept(Guid id, CancellationToken cancellationToken) => suggestions.AcceptAsync(id, cancellationToken);

    /// <summary>Aplica valores corregidos por el agente y los envía a P27 como retroalimentación.</summary>
    [HttpPost("{id:guid}/correct")]
    public Task<SuggestionDto> Correct(Guid id, CorrectSuggestionRequest request, CancellationToken cancellationToken) =>
        suggestions.CorrectAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/dismiss")]
    public Task<SuggestionDto> Dismiss(Guid id, CancellationToken cancellationToken) => suggestions.DismissAsync(id, cancellationToken);
}

/// <summary>Endpoints para clientes de integración con JWT de client credentials (p. ej. el servicio de IA P27).</summary>
[Route("api/v1/integration")]
public sealed class IntegrationController(ITicketQueries queries, SuggestionService suggestions) : ApiControllerBase
{
    public const int MaxBatch = 50;

    /// <summary>Tickets sin categoría ni sugerencia pendiente (scope <c>tickets:read</c>).</summary>
    [HttpGet("tickets/unclassified")]
    [Authorize(Policy = Policies.IntegrationRead)]
    public Task<IReadOnlyList<UnclassifiedTicketDto>> Unclassified([FromQuery] int limit = 20, CancellationToken cancellationToken = default) =>
        queries.ListUnclassifiedAsync(Math.Clamp(limit, 1, MaxBatch), cancellationToken);

    /// <summary>Registra una sugerencia para revisión humana (scope <c>suggestions:write</c>).</summary>
    [HttpPost("tickets/{id:guid}/suggestions")]
    [Authorize(Policy = Policies.IntegrationWrite)]
    [ProducesResponseType(typeof(SuggestionDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Push(Guid id, PushSuggestionRequest request, CancellationToken cancellationToken)
    {
        var suggestion = await suggestions.PushAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, suggestion);
    }
}

/// <summary>Catálogos: colas, categorías, personal asignable y matriz de prioridad.</summary>
[Route("api/v1")]
[Authorize(Policy = Policies.User)]
public sealed class CatalogController(CatalogService catalog) : ApiControllerBase
{
    [HttpGet("teams")]
    [Authorize(Policy = Policies.Staff)]
    public Task<IReadOnlyList<TeamDto>> Teams(CancellationToken cancellationToken) => catalog.ListTeamsAsync(cancellationToken);

    [HttpPut("teams/{id:guid}/members")]
    [Authorize(Policy = Policies.Admin)]
    public Task<TeamDto> SetMembers(Guid id, SetTeamMembersRequest request, CancellationToken cancellationToken) =>
        catalog.SetMembersAsync(id, request, cancellationToken);

    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken cancellationToken) => catalog.ListCategoriesAsync(cancellationToken);

    [HttpGet("agents")]
    [Authorize(Policy = Policies.Staff)]
    public Task<IReadOnlyList<AgentDto>> Agents([FromQuery] Guid? teamId, CancellationToken cancellationToken) => catalog.ListAgentsAsync(teamId, cancellationToken);

    /// <summary>Matriz ITIL impacto × urgencia → prioridad.</summary>
    [HttpGet("priority-matrix")]
    public IReadOnlyList<PriorityMatrixCellDto> PriorityMatrix() => CatalogService.PriorityMatrixCells();
}

[Route("api/v1/customers")]
[Authorize(Policy = Policies.Staff)]
public sealed class CustomersController(CustomerService customers) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerDto>> Search([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        customers.SearchAsync(search, page, pageSize, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<CustomerDto> Get(Guid id, CancellationToken cancellationToken) => customers.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = Policies.Supervisor)]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customers.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Supervisor)]
    public Task<CustomerDto> Update(Guid id, SaveCustomerRequest request, CancellationToken cancellationToken) =>
        customers.UpdateAsync(id, request, cancellationToken);

    /// <summary>Derecho de eliminación (LOPDP art. 15): anonimiza al cliente y a sus usuarios del portal.</summary>
    [HttpPost("{id:guid}/anonymize")]
    [Authorize(Policy = Policies.Admin)]
    public Task<CustomerDto> Anonymize(Guid id, CancellationToken cancellationToken) => customers.AnonymizeAsync(id, cancellationToken);
}

[Route("api/v1/users")]
[Authorize(Policy = Policies.Admin)]
public sealed class UsersController(IUserAdminService users) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserDto>> List(CancellationToken cancellationToken) => users.ListAsync(cancellationToken);

    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await users.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetStatus(Guid id, SetUserStatusRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await users.SetActiveAsync(id, request.IsActive, cancellationToken);
        return NoContent();
    }
}

/// <summary>Reglas de SLA y calendarios laborales con feriados.</summary>
[Route("api/v1")]
[Authorize(Policy = Policies.Staff)]
public sealed class SlaController(SlaAdminService sla) : ApiControllerBase
{
    [HttpGet("sla-policies")]
    public Task<IReadOnlyList<SlaPolicyDto>> ListPolicies(CancellationToken cancellationToken) => sla.ListPoliciesAsync(cancellationToken);

    [HttpPut("sla-policies/{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public Task<SlaPolicyDto> UpdatePolicy(Guid id, UpdateSlaPolicyRequest request, CancellationToken cancellationToken) =>
        sla.UpdatePolicyAsync(id, request, cancellationToken);

    [HttpGet("calendars")]
    public Task<IReadOnlyList<CalendarDto>> Calendars(CancellationToken cancellationToken) => sla.ListCalendarsAsync(cancellationToken);

    [HttpPost("calendars/{id:guid}/holidays")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(typeof(HolidayDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddHoliday(Guid id, CreateHolidayRequest request, CancellationToken cancellationToken)
    {
        var holiday = await sla.AddHolidayAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, holiday);
    }

    [HttpDelete("calendars/{id:guid}/holidays/{holidayId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveHoliday(Guid id, Guid holidayId, CancellationToken cancellationToken)
    {
        await sla.RemoveHolidayAsync(id, holidayId, cancellationToken);
        return NoContent();
    }
}

[Route("api/v1/notifications")]
[Authorize(Policy = Policies.User)]
public sealed class NotificationsController(NotificationService notifications) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<NotificationDto>> List([FromQuery] bool unreadOnly, CancellationToken cancellationToken) =>
        notifications.ListAsync(unreadOnly, cancellationToken);

    [HttpGet("unread-count")]
    public Task<UnreadCountDto> UnreadCount(CancellationToken cancellationToken) => notifications.UnreadCountAsync(cancellationToken);

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await notifications.MarkReadAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await notifications.MarkAllReadAsync(cancellationToken);
        return NoContent();
    }
}

/// <summary>Reportes de supervisión: tiempos medios, cumplimiento de SLA, CSAT y carga por agente.</summary>
[Route("api/v1/reports")]
[Authorize(Policy = Policies.Supervisor)]
public sealed class ReportsController(ReportService reports) : ApiControllerBase
{
    [HttpGet("overview")]
    public Task<ReportOverviewDto> Overview([FromQuery] ReportQuery query, CancellationToken cancellationToken) => reports.OverviewAsync(query, cancellationToken);

    [HttpGet("agents")]
    public Task<IReadOnlyList<AgentWorkloadRow>> Agents([FromQuery] ReportQuery query, CancellationToken cancellationToken) => reports.AgentsAsync(query, cancellationToken);

    /// <summary>Exporta los tickets del periodo en CSV (UTF-8 con BOM para Excel), sin datos personales del cliente.</summary>
    [HttpGet("tickets.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] ReportQuery query, CancellationToken cancellationToken)
    {
        var csv = await reports.ExportCsvAsync(query, cancellationToken);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", "tickets.csv");
    }
}
