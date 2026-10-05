using Helpdesk.Api.Infrastructure;
using Helpdesk.Api.Security;
using Helpdesk.Application.Attachments;
using Helpdesk.Application.Suggestions;
using Helpdesk.Application.Tickets;
using Helpdesk.Contracts.Common;
using Helpdesk.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Helpdesk.Api.Controllers;

/// <summary>Tickets: listado según rol, detalle, apertura, máquina de estados, comentarios, adjuntos y encuesta.</summary>
[Route("api/v1/tickets")]
[Authorize(Policy = Policies.User)]
public sealed class TicketsController(TicketService tickets, AttachmentService attachments, SuggestionService suggestions) : ApiControllerBase
{
    /// <summary>Lista paginada. Un agente ve su cola y lo asignado; un cliente, solo sus tickets.</summary>
    [HttpGet]
    public Task<PagedResult<TicketListItemDto>> List([FromQuery] TicketQuery query, CancellationToken cancellationToken) =>
        tickets.ListAsync(query, cancellationToken);

    /// <summary>Detalle con comentarios, adjuntos, historial, SLA y acciones permitidas. Devuelve ETag con la versión.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<TicketDetailDto> Get(Guid id, CancellationToken cancellationToken) => WithETag(await tickets.GetAsync(id, cancellationToken));

    /// <summary>Abre un ticket: calcula prioridad (impacto × urgencia), regla de SLA y vencimientos.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var created = WithETag(await tickets.CreateAsync(request, cancellationToken));
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = Policies.Staff)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<TicketDetailDto> Assign(Guid id, AssignRequest request, CancellationToken cancellationToken) =>
        WithETag(await tickets.AssignAsync(id, request, IfMatchVersion(), cancellationToken));

    /// <summary>Escalamiento funcional: mueve el ticket a otra cola (vuelve a Nuevo).</summary>
    [HttpPost("{id:guid}/transfer")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<TicketDetailDto> Transfer(Guid id, TransferRequest request, CancellationToken cancellationToken) =>
        WithETag(await tickets.TransferAsync(id, request, IfMatchVersion(), cancellationToken));

    /// <summary>Ejecuta una transición de la máquina de estados (iniciar, espera, reanudar, resolver, reabrir, cerrar, cancelar).</summary>
    [HttpPost("{id:guid}/transitions")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<TicketDetailDto> Transition(Guid id, TransitionRequest request, CancellationToken cancellationToken) =>
        WithETag(await tickets.TransitionAsync(id, request, IfMatchVersion(), cancellationToken));

    /// <summary>Cambia categoría, impacto y urgencia; recalcula prioridad y SLA.</summary>
    [HttpPut("{id:guid}/classification")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<TicketDetailDto> Classify(Guid id, ClassifyRequest request, CancellationToken cancellationToken) =>
        WithETag(await tickets.ClassifyAsync(id, request, IfMatchVersion(), cancellationToken));

    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Comment(Guid id, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        var comment = await tickets.AddCommentAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, comment);
    }

    /// <summary>Sube un adjunto (PDF, PNG, JPEG o TXT, máx. 5 MB). El tipo se valida por la firma del archivo.</summary>
    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Upload(Guid id, IFormFile file, [FromForm] bool isInternal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var stream = file.OpenReadStream();
        var attachment = await attachments.UploadAsync(id, file.FileName, stream, isInternal, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, attachment);
    }

    /// <summary>Encuesta de satisfacción (solo el cliente). Calificar un ticket resuelto lo confirma y lo cierra.</summary>
    [HttpPost("{id:guid}/survey")]
    [Authorize(Policy = Policies.Customer)]
    public async Task<TicketDetailDto> Survey(Guid id, SurveyRequest request, CancellationToken cancellationToken) =>
        WithETag(await tickets.SubmitSurveyAsync(id, request, cancellationToken));

    [HttpGet("{id:guid}/suggestions")]
    [Authorize(Policy = Policies.Staff)]
    public Task<IReadOnlyList<SuggestionDto>> Suggestions(Guid id, CancellationToken cancellationToken) =>
        suggestions.ListForTicketAsync(id, cancellationToken);

    /// <summary>Pide una sugerencia al clasificador (P27). Se procesa en segundo plano y se avisa por SignalR.</summary>
    [HttpPost("{id:guid}/suggestions/request")]
    [Authorize(Policy = Policies.Staff)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestSuggestion(Guid id, CancellationToken cancellationToken)
    {
        await suggestions.RequestAsync(id, cancellationToken);
        return Accepted();
    }

    private TicketDetailDto WithETag(TicketDetailDto detail)
    {
        SetETag(detail.Version);
        return detail;
    }
}
