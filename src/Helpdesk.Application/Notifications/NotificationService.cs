using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Events;
using Helpdesk.Application.Exceptions;
using Helpdesk.Contracts.Notifications;

namespace Helpdesk.Application.Notifications;

/// <summary>Centro de avisos del usuario: cada quien lee y marca solo sus propias notificaciones.</summary>
public sealed class NotificationService(INotificationRepository notifications, IUnitOfWork unitOfWork, ICurrentUser currentUser)
{
    public const int MaxItems = 50;

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(bool unreadOnly, CancellationToken cancellationToken)
    {
        var items = await notifications.ListAsync(currentUser.UserId, unreadOnly, MaxItems, cancellationToken);
        return [.. items.Select(TicketEventDispatcher.ToDto)];
    }

    public async Task<UnreadCountDto> UnreadCountAsync(CancellationToken cancellationToken) =>
        new(await notifications.CountUnreadAsync(currentUser.UserId, cancellationToken));

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetAsync(id, cancellationToken);
        if (notification is null || notification.UserId != currentUser.UserId)
        {
            throw new NotFoundException("Notificación");
        }

        notification.MarkAsRead();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<int> MarkAllReadAsync(CancellationToken cancellationToken) =>
        notifications.MarkAllReadAsync(currentUser.UserId, cancellationToken);
}
