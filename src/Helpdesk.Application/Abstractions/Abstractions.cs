using Helpdesk.Application.Security;
using Helpdesk.Contracts.Common;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Catalog;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Application.Abstractions;

/// <summary>Usuario o cliente de integración que hace la petición actual.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    ActorRole Role { get; }

    Guid? CustomerId { get; }

    IReadOnlyCollection<string> Scopes { get; }

    Actor Actor { get; }

    /// <summary>Contexto de acceso con las colas del usuario (se consulta una vez por petición).</summary>
    Task<AccessContext> GetAccessAsync(CancellationToken cancellationToken);
}

/// <summary>Confirma los cambios y despacha los eventos de dominio después de guardar.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Exige que el ticket no haya cambiado desde que el cliente lo leyó (cabecera If-Match).</summary>
    void ExpectVersion(Ticket ticket, uint expectedVersion);
}

public interface ITicketRepository
{
    Task<Ticket?> GetAsync(Guid id, bool includeDetails, CancellationToken cancellationToken);

    /// <summary>Ticket (con detalle) dueño de un adjunto.</summary>
    Task<Ticket?> GetByAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken);

    void Add(Ticket ticket);

    /// <summary>Tickets con avisos o vencimientos de SLA pendientes, o resueltos listos para cierre automático.</summary>
    Task<IReadOnlyList<Ticket>> GetDueForSlaEvaluationAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetUnclassifiedWithoutSuggestionAsync(DateTimeOffset since, int limit, CancellationToken cancellationToken);
}

public interface ISuggestionRepository
{
    Task<TicketSuggestion?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketSuggestion>> ListForTicketAsync(Guid ticketId, CancellationToken cancellationToken);

    Task<bool> HasPendingAsync(Guid ticketId, CancellationToken cancellationToken);

    void Add(TicketSuggestion suggestion);
}

public interface ICatalogRepository
{
    Task<IReadOnlyList<Team>> ListTeamsAsync(bool includeMembers, CancellationToken cancellationToken);

    Task<Team?> GetTeamAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken cancellationToken);

    Task<Category?> GetCategoryAsync(Guid id, CancellationToken cancellationToken);
}

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<Customer>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<string> NextCodeAsync(CancellationToken cancellationToken);

    void Add(Customer customer);
}

public interface ISlaRepository
{
    Task<IReadOnlyList<SlaPolicy>> ListPoliciesAsync(CancellationToken cancellationToken);

    Task<SlaPolicy?> GetPolicyAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BusinessCalendar>> ListCalendarsAsync(CancellationToken cancellationToken);

    Task<BusinessCalendar?> GetCalendarAsync(Guid id, CancellationToken cancellationToken);

    Task<BusinessCalendar?> GetCalendarByCodeAsync(string code, CancellationToken cancellationToken);
}

public interface INotificationRepository
{
    void Add(Notification notification);

    Task<IReadOnlyList<Notification>> ListAsync(Guid userId, bool unreadOnly, int limit, CancellationToken cancellationToken);

    Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Fila mínima para calcular reportes sin cargar agregados completos.</summary>
public sealed record TicketReportRow(
    Guid Id,
    long Number,
    DateTimeOffset CreatedAt,
    TicketStatus Status,
    Priority Priority,
    string? CategoryName,
    Guid TeamId,
    string TeamName,
    Guid? AssigneeId,
    string CustomerCode,
    DateTimeOffset? FirstRespondedAt,
    bool FirstResponseBreached,
    int? FirstResponseBusinessMinutes,
    DateTimeOffset? ResolvedAt,
    bool ResolutionBreached,
    int? ResolutionBusinessMinutes,
    int? SurveyRating);

/// <summary>Consultas de lectura (proyecciones directas a DTO).</summary>
public interface ITicketQueries
{
    Task<PagedResult<TicketListItemDto>> ListAsync(TicketQuery query, AccessContext access, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnclassifiedTicketDto>> ListUnclassifiedAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Tickets creados o resueltos en el rango, más todos los abiertos (para la foto actual).</summary>
    Task<IReadOnlyList<TicketReportRow>> ReportRowsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? teamId, CancellationToken cancellationToken);
}

/// <summary>Datos de personas que viven en Identity y que la capa de aplicación necesita.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> GetTeamIdsAsync(Guid userId, CancellationToken cancellationToken);

    Task<StaffMember?> GetActiveStaffAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetTeamLeadIdsAsync(Guid teamId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetSupervisorIdsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetCustomerUserIdsAsync(Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StaffMember>> ListStaffAsync(Guid? teamId, CancellationToken cancellationToken);

    /// <summary>Versión del aviso de privacidad aceptada, leída de la base (un token emitido antes de aceptar no la trae).</summary>
    Task<string?> GetPrivacyNoticeVersionAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record StaffMember(Guid Id, string FullName, ActorRole Role, IReadOnlySet<Guid> TeamIds);

/// <summary>Almacenamiento de adjuntos fuera de la base de datos.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

/// <summary>Resultado de clasificar un texto con el servicio de IA (P27).</summary>
public sealed record NlpClassification(string Provider, string ModelVersion, Guid? PredictionId, SuggestedClassification Suggestion);

/// <summary>Cliente del clasificador nlp-tickets-es (P27).</summary>
public interface INlpClassifier
{
    bool IsEnabled { get; }

    bool CanSendFeedback { get; }

    Task<NlpClassification?> ClassifyAsync(string text, TicketChannel channel, string externalRef, CancellationToken cancellationToken);

    Task SendFeedbackAsync(Guid predictionId, string? categoryCode, string? teamCode, Urgency? urgency, CancellationToken cancellationToken);
}

/// <summary>Bandeja en memoria de tickets que esperan sugerencia del clasificador.</summary>
public interface ISuggestionInbox
{
    void Enqueue(Guid ticketId);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}

/// <summary>Envío en tiempo real (SignalR). Solo identificadores y estado, nunca contenido.</summary>
public interface IRealtimeNotifier
{
    Task TicketChangedAsync(TicketChangedMessage message, TicketAudience audience, CancellationToken cancellationToken);

    Task NotificationAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken);
}

/// <summary>Quiénes deben enterarse de un cambio: la cola, el asignado y los usuarios del cliente.</summary>
public sealed record TicketAudience(Guid TeamId, Guid? AssigneeId, Guid CustomerId);

public sealed record DomainEventEnvelope(Entity Source, IDomainEvent Event);

/// <summary>
/// Se ejecuta en dos fases: <see cref="DispatchAsync"/> deja notificaciones listas para guardar y
/// <see cref="PublishPendingAsync"/> envía el tiempo real solo después de confirmar en la base.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<DomainEventEnvelope> events, CancellationToken cancellationToken);

    Task PublishPendingAsync(CancellationToken cancellationToken);
}
