using System.Globalization;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Sla;
using Helpdesk.Application.Tickets;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Tickets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helpdesk.Application.Events;

/// <summary>
/// Convierte los eventos de dominio en notificaciones persistentes (centro de avisos) y en mensajes de tiempo real.
/// Los textos no incluyen datos personales del cliente: número, prioridad y vencimiento (LOPDP art. 10 e).
/// </summary>
public sealed partial class TicketEventDispatcher(
    INotificationRepository notifications,
    IRealtimeNotifier realtime,
    IUserDirectory users,
    ICurrentUser currentUser,
    IOptions<HelpdeskOptions> options,
    TimeProvider clock,
    ILogger<TicketEventDispatcher> logger) : IDomainEventDispatcher
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-EC");
    private readonly List<(Guid UserId, Notification Notification)> _pendingNotifications = [];
    private readonly Dictionary<Guid, (TicketChangedMessage Message, TicketAudience Audience)> _pendingChanges = [];

    public async Task DispatchAsync(IReadOnlyList<DomainEventEnvelope> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var (source, domainEvent) in events)
        {
            if (source is not Ticket ticket)
            {
                continue;
            }

            var kind = await HandleAsync(ticket, domainEvent, cancellationToken);
            QueueChange(ticket, kind);
        }
    }

    public async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var (userId, notification) in _pendingNotifications)
            {
                await realtime.NotificationAsync(userId, ToDto(notification), cancellationToken);
            }

            foreach (var (message, audience) in _pendingChanges.Values)
            {
                await realtime.TicketChangedAsync(message, audience, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El tiempo real es un plus: si falla, el dato ya está guardado y el usuario lo verá al recargar.
            LogRealtimeFailed(logger, ex);
        }
        finally
        {
            _pendingNotifications.Clear();
            _pendingChanges.Clear();
        }
    }

    public static NotificationDto ToDto(Notification n) => new(n.Id, n.Type, n.Title, n.Message, n.TicketId, n.IsRead, n.CreatedAt);

    /// <summary>Encola una notificación para usuarios concretos (también la usa el servicio de sugerencias).</summary>
    public void Stage(IEnumerable<Guid> userIds, Ticket ticket, NotificationType type, string title, string message)
    {
        var self = currentUser.IsAuthenticated ? currentUser.UserId : (Guid?)null;
        foreach (var userId in userIds.Distinct().Where(id => id != self))
        {
            var notification = new Notification(userId, ticket.Id, type, title, message, clock.GetUtcNow());
            notifications.Add(notification);
            _pendingNotifications.Add((userId, notification));
        }
    }

    public void QueueChange(Ticket ticket, TicketChangeKind kind)
    {
        var message = new TicketChangedMessage(ticket.Id, ticket.DisplayNumber, ticket.Status, ticket.Priority, SlaStateCalculator.Calculate(ticket, clock.GetUtcNow()), kind, clock.GetUtcNow());
        var audience = new TicketAudience(ticket.TeamId, ticket.AssigneeId, ticket.CustomerId);
        if (!_pendingChanges.TryGetValue(ticket.Id, out var existing) || Rank(kind) >= Rank(existing.Message.Kind))
        {
            _pendingChanges[ticket.Id] = (message, audience);
        }
    }

    private async Task<TicketChangeKind> HandleAsync(Ticket ticket, Domain.Common.IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var number = ticket.DisplayNumber;
        switch (domainEvent)
        {
            case TicketCreated:
                Stage(await users.GetTeamLeadIdsAsync(ticket.TeamId, cancellationToken), ticket, NotificationType.TicketCreated,
                    $"Nuevo ticket {number}", $"Prioridad {PriorityName(ticket.Priority)}. Primera respuesta antes de {Format(ticket.FirstResponseDueAt)}.");
                return TicketChangeKind.Created;

            case TicketAssigned assigned:
                Stage([assigned.AssigneeId], ticket, NotificationType.TicketAssigned,
                    $"Te asignaron {number}", $"Prioridad {PriorityName(ticket.Priority)}. Resolver antes de {Format(ticket.ResolutionDueAt)}.");
                return TicketChangeKind.Assigned;

            case TicketTransferred:
                Stage(await users.GetTeamLeadIdsAsync(ticket.TeamId, cancellationToken), ticket, NotificationType.TicketCreated,
                    $"{number} transferido a tu cola", $"Prioridad {PriorityName(ticket.Priority)}.");
                return TicketChangeKind.Transferred;

            case TicketStatusChanged changed:
                await OnStatusChangedAsync(ticket, changed, cancellationToken);
                return TicketChangeKind.StatusChanged;

            case TicketCommentAdded comment when !comment.IsInternal:
                await OnPublicCommentAsync(ticket, comment, cancellationToken);
                return TicketChangeKind.CommentAdded;

            case TicketCommentAdded:
                return TicketChangeKind.CommentAdded;

            case SlaWarningRaised warning:
                Stage(await ResponsiblesAsync(ticket, cancellationToken), ticket, NotificationType.SlaWarning,
                    $"{number}: SLA en riesgo", $"La {MetricName(warning.Metric)} vence {Format(warning.DueAt)}.");
                return TicketChangeKind.SlaAlert;

            case SlaBreached breach:
                var escalation = new List<Guid>(await ResponsiblesAsync(ticket, cancellationToken));
                escalation.AddRange(await users.GetTeamLeadIdsAsync(ticket.TeamId, cancellationToken));
                escalation.AddRange(await users.GetSupervisorIdsAsync(cancellationToken));
                Stage(escalation, ticket, NotificationType.SlaBreached,
                    $"{number}: SLA incumplido", $"La {MetricName(breach.Metric)} venció {Format(breach.DueAt)}. Escalado a líderes y supervisión.");
                return TicketChangeKind.SlaAlert;

            default:
                return TicketChangeKind.Updated;
        }
    }

    private async Task OnStatusChangedAsync(Ticket ticket, TicketStatusChanged changed, CancellationToken cancellationToken)
    {
        var number = ticket.DisplayNumber;
        var customerUsers = await users.GetCustomerUserIdsAsync(ticket.CustomerId, cancellationToken);
        var message = changed.To switch
        {
            TicketStatus.InProgress when changed.From == TicketStatus.Assigned => "Un agente empezó a trabajar en tu caso.",
            TicketStatus.OnHold => "Necesitamos información adicional: revisa los comentarios del ticket.",
            TicketStatus.Resolved => "Marcamos tu caso como resuelto. Confírmalo y califica la atención, o reábrelo si sigue el problema.",
            TicketStatus.Closed when changed.ChangedBy == Domain.Common.ActorRole.System => "Cerramos tu caso porque no se reabrió en el plazo.",
            TicketStatus.Cancelled => "El ticket fue cancelado.",
            _ => null,
        };

        if (message is not null)
        {
            Stage(customerUsers, ticket, NotificationType.StatusChanged, $"{number}: {StatusName(changed.To)}", message);
        }

        if (changed is { From: TicketStatus.Resolved, To: TicketStatus.InProgress } && ticket.AssigneeId is { } assignee)
        {
            Stage([assignee], ticket, NotificationType.StatusChanged, $"{number} fue reabierto", "El cliente indicó que el problema continúa.");
        }
    }

    private async Task OnPublicCommentAsync(Ticket ticket, TicketCommentAdded comment, CancellationToken cancellationToken)
    {
        var number = ticket.DisplayNumber;
        if (comment.AuthorRole == Domain.Common.ActorRole.Customer)
        {
            Stage(await ResponsiblesAsync(ticket, cancellationToken), ticket, NotificationType.CommentAdded, $"{number}: respuesta del cliente", "El cliente agregó un comentario.");
        }
        else
        {
            Stage(await users.GetCustomerUserIdsAsync(ticket.CustomerId, cancellationToken), ticket, NotificationType.CommentAdded, $"{number}: nueva respuesta", "Un agente respondió a tu caso.");
        }
    }

    /// <summary>El asignado; si no hay, los líderes de la cola.</summary>
    private async Task<IReadOnlyList<Guid>> ResponsiblesAsync(Ticket ticket, CancellationToken cancellationToken) =>
        ticket.AssigneeId is { } assignee ? [assignee] : await users.GetTeamLeadIdsAsync(ticket.TeamId, cancellationToken);

    private string Format(DateTimeOffset instant)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        return "el " + TimeZoneInfo.ConvertTime(instant, zone).ToString("dd/MM HH:mm", Spanish);
    }

    private static int Rank(TicketChangeKind kind) => kind switch
    {
        TicketChangeKind.Updated or TicketChangeKind.CommentAdded => 0,
        TicketChangeKind.SuggestionReady => 1,
        _ => 2,
    };

    private static string MetricName(SlaMetric metric) => metric == SlaMetric.FirstResponse ? "primera respuesta" : "resolución";

    public static string PriorityName(Priority priority) => priority switch
    {
        Priority.Critical => "Crítica",
        Priority.High => "Alta",
        Priority.Medium => "Media",
        Priority.Low => "Baja",
        _ => "Planificación",
    };

    public static string StatusName(TicketStatus status) => status switch
    {
        TicketStatus.New => "Nuevo",
        TicketStatus.Assigned => "Asignado",
        TicketStatus.InProgress => "En proceso",
        TicketStatus.OnHold => "En espera",
        TicketStatus.Resolved => "Resuelto",
        TicketStatus.Closed => "Cerrado",
        _ => "Cancelado",
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo enviar una notificación en tiempo real")]
    private static partial void LogRealtimeFailed(ILogger logger, Exception exception);
}
