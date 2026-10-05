using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Notifications;

public enum NotificationType
{
    TicketCreated,
    TicketAssigned,
    StatusChanged,
    CommentAdded,
    SlaWarning,
    SlaBreached,
    SuggestionReady,
}

/// <summary>Notificación persistente del centro de avisos. No guarda datos personales del cliente.</summary>
public sealed class Notification : Entity
{
    public const int TitleMaxLength = 150;
    public const int MessageMaxLength = 500;

    private Notification()
    {
        Title = string.Empty;
        Message = string.Empty;
    }

    public Notification(Guid userId, Guid? ticketId, NotificationType type, string title, string message, DateTimeOffset createdAt)
    {
        UserId = userId;
        TicketId = ticketId;
        Type = type;
        Title = Truncate(title, TitleMaxLength);
        Message = Truncate(message, MessageMaxLength);
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }

    public Guid? TicketId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Title { get; private set; }

    public string Message { get; private set; }

    public bool IsRead { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkAsRead() => IsRead = true;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
