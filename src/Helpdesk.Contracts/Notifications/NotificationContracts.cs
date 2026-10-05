using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Contracts.Notifications;

public sealed record NotificationDto(Guid Id, NotificationType Type, string Title, string Message, Guid? TicketId, bool IsRead, DateTimeOffset CreatedAt);

public sealed record UnreadCountDto(int Count);

/// <summary>Qué pasó con un ticket; el front decide si refresca la lista o el detalle.</summary>
public enum TicketChangeKind
{
    Created,
    Updated,
    StatusChanged,
    Assigned,
    Transferred,
    CommentAdded,
    SlaAlert,
    SuggestionReady,
}

/// <summary>
/// Aviso en tiempo real. Solo lleva identificadores y estado: el contenido se vuelve a pedir a la API,
/// que aplica la autorización (el hub nunca envía datos que el usuario no podría leer).
/// </summary>
public sealed record TicketChangedMessage(Guid TicketId, string Number, TicketStatus Status, Priority Priority, SlaState SlaState, TicketChangeKind Kind, DateTimeOffset OccurredAt);

/// <summary>Nombres de los métodos del hub que invoca el servidor.</summary>
public static class HubMethods
{
    public const string TicketChanged = "TicketChanged";
    public const string NotificationReceived = "NotificationReceived";
}
