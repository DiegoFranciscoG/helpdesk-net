using Helpdesk.Domain.Tickets;

namespace Helpdesk.Contracts.Tickets;

/// <summary>Semáforo del SLA que ve el usuario.</summary>
public enum SlaState
{
    OnTrack,
    AtRisk,
    Breached,
    Paused,
    Met,
    NotApplicable,
}

/// <summary>Acciones que el usuario actual puede ejecutar sobre un ticket (las calcula el servidor).</summary>
public enum TicketAction
{
    Assign,
    Start,
    Hold,
    Resume,
    Resolve,
    Reopen,
    Close,
    Cancel,
    Transfer,
    Classify,
    Comment,
    InternalComment,
    Upload,
    Survey,
    ReviewSuggestion,
}

/// <summary>Transiciones que se piden por <c>POST /tickets/{id}/transitions</c>.</summary>
public enum TransitionAction
{
    Start,
    Hold,
    Resume,
    Resolve,
    Reopen,
    Close,
    Cancel,
}

public enum TicketSort
{
    Newest,
    Priority,
    DueDate,
}

public sealed class CreateTicketRequest
{
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public TicketChannel Channel { get; set; } = TicketChannel.Portal;

    public Impact Impact { get; set; } = Impact.Low;

    public Urgency Urgency { get; set; } = Urgency.Medium;

    public Guid? CategoryId { get; set; }

    /// <summary>Obligatorio cuando lo registra el personal; los clientes lo omiten (se usa su propio cliente).</summary>
    public Guid? CustomerId { get; set; }
}

public sealed class TicketQuery
{
    public TicketStatus? Status { get; set; }

    public bool OpenOnly { get; set; }

    public Priority? Priority { get; set; }

    public Guid? TeamId { get; set; }

    public Guid? AssigneeId { get; set; }

    public bool AssignedToMe { get; set; }

    public Guid? CustomerId { get; set; }

    public SlaState? Sla { get; set; }

    public string? Search { get; set; }

    public TicketSort Sort { get; set; } = TicketSort.Newest;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class AssignRequest
{
    public Guid AssigneeId { get; set; }
}

public sealed class TransferRequest
{
    public Guid TeamId { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class TransitionRequest
{
    public TransitionAction Action { get; set; }

    /// <summary>Motivo: obligatorio para poner en espera, reabrir y cancelar.</summary>
    public string? Reason { get; set; }

    /// <summary>Resumen visible para el cliente: obligatorio al resolver.</summary>
    public string? ResolutionSummary { get; set; }
}

public sealed class ClassifyRequest
{
    public Guid? CategoryId { get; set; }

    public Impact Impact { get; set; }

    public Urgency Urgency { get; set; }
}

public sealed class CreateCommentRequest
{
    public string Body { get; set; } = string.Empty;

    public bool IsInternal { get; set; }
}

public sealed class SurveyRequest
{
    public int Rating { get; set; } = 5;

    public string? Comment { get; set; }
}

public sealed record SlaInfoDto(
    SlaState State,
    string PolicyName,
    DateTimeOffset FirstResponseDueAt,
    DateTimeOffset? FirstRespondedAt,
    bool FirstResponseBreached,
    DateTimeOffset ResolutionDueAt,
    DateTimeOffset? ResolvedAt,
    bool ResolutionBreached,
    bool IsPaused,
    int PausedBusinessMinutes,
    int EscalationLevel);

public sealed record TicketListItemDto(
    Guid Id,
    string Number,
    string Title,
    TicketStatus Status,
    Priority Priority,
    Impact Impact,
    Urgency Urgency,
    string? CategoryName,
    Guid TeamId,
    string TeamName,
    Guid? AssigneeId,
    string? AssigneeName,
    string CustomerCode,
    string CustomerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset FirstResponseDueAt,
    DateTimeOffset? FirstRespondedAt,
    DateTimeOffset ResolutionDueAt,
    SlaState SlaState,
    int EscalationLevel);

public sealed record CommentDto(Guid Id, Guid AuthorId, string AuthorName, bool AuthorIsStaff, string Body, bool IsInternal, DateTimeOffset CreatedAt);

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, bool IsInternal, string UploadedByName, DateTimeOffset CreatedAt);

public sealed record StatusChangeDto(TicketStatus? FromStatus, TicketStatus ToStatus, string? ChangedByName, string? Reason, DateTimeOffset ChangedAt);

public sealed record SurveyDto(int Rating, string? Comment, DateTimeOffset SubmittedAt);

public sealed record TicketDetailDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    TicketStatus Status,
    TicketChannel Channel,
    Impact Impact,
    Urgency Urgency,
    Priority Priority,
    Guid? CategoryId,
    string? CategoryName,
    Guid TeamId,
    string TeamName,
    Guid? AssigneeId,
    string? AssigneeName,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ResolutionSummary,
    int ReopenCount,
    DateTimeOffset? AutoCloseAt,
    DateTimeOffset? ClosedAt,
    SlaInfoDto Sla,
    IReadOnlyList<CommentDto> Comments,
    IReadOnlyList<AttachmentDto> Attachments,
    IReadOnlyList<StatusChangeDto> History,
    SurveyDto? Survey,
    IReadOnlyList<TicketAction> AllowedActions,
    uint Version);

/// <summary>Sugerencia del clasificador con los nombres ya resueltos contra el catálogo local.</summary>
public sealed record SuggestionDto(
    Guid Id,
    Guid TicketId,
    SuggestionStatus Status,
    SuggestionSource Source,
    string Provider,
    string ModelVersion,
    string? CategoryCode,
    Guid? CategoryId,
    string? CategoryName,
    string? TeamCode,
    Guid? TeamId,
    string? TeamName,
    Urgency? Urgency,
    Priority? ResultingPriority,
    decimal? CategoryConfidence,
    decimal? TeamConfidence,
    string? Decision,
    IReadOnlyList<string> ReviewReasons,
    DateTimeOffset CreatedAt,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt);

public sealed class CorrectSuggestionRequest
{
    public Guid? CategoryId { get; set; }

    public Guid? TeamId { get; set; }

    public Urgency? Urgency { get; set; }
}

/// <summary>Ticket pendiente de clasificar que lee un cliente de integración (scope <c>tickets:read</c>).</summary>
public sealed record UnclassifiedTicketDto(Guid Id, string Number, string Title, string Description, TicketChannel Channel, DateTimeOffset CreatedAt);

/// <summary>Sugerencia que empuja un cliente de integración (scope <c>suggestions:write</c>).</summary>
public sealed class PushSuggestionRequest
{
    public string Provider { get; set; } = string.Empty;

    public string ModelVersion { get; set; } = string.Empty;

    public string? CategoryCode { get; set; }

    public string? TeamCode { get; set; }

    public Urgency? Urgency { get; set; }

    public decimal? CategoryConfidence { get; set; }

    public decimal? TeamConfidence { get; set; }

    public string? Decision { get; set; }

    public Guid? ExternalPredictionId { get; set; }
}
