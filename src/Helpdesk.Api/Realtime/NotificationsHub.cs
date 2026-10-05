using Helpdesk.Api.Security;
using Helpdesk.Application.Abstractions;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Helpdesk.Api.Realtime;

/// <summary>
/// Hub de solo servidor → cliente. Al conectar, cada usuario entra a los grupos que le corresponden:
/// sus colas (agentes), "supervisors" (supervisión y administración) o su cliente (portal).
/// </summary>
[Authorize(Policy = Policies.User)]
public sealed class NotificationsHub(ICurrentUser currentUser) : Hub
{
    public const string Supervisors = "supervisors";

    public static string TeamGroup(Guid teamId) => $"team:{teamId}";

    public static string CustomerGroup(Guid customerId) => $"customer:{customerId}";

    public override async Task OnConnectedAsync()
    {
        var access = await currentUser.GetAccessAsync(Context.ConnectionAborted);
        if (access.SeesEverything)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, Supervisors);
        }

        foreach (var teamId in access.TeamIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));
        }

        if (access.Role == ActorRole.Customer && access.CustomerId is { } customerId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CustomerGroup(customerId));
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Implementación de <see cref="IRealtimeNotifier"/> con SignalR.</summary>
internal sealed class SignalRRealtimeNotifier(IHubContext<NotificationsHub> hub) : IRealtimeNotifier
{
    public async Task TicketChangedAsync(TicketChangedMessage message, TicketAudience audience, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audience);
        string[] groups = [NotificationsHub.TeamGroup(audience.TeamId), NotificationsHub.Supervisors, NotificationsHub.CustomerGroup(audience.CustomerId)];
        await hub.Clients.Groups(groups).SendAsync(HubMethods.TicketChanged, message, cancellationToken);
        if (audience.AssigneeId is { } assignee)
        {
            await hub.Clients.User(assignee.ToString()).SendAsync(HubMethods.TicketChanged, message, cancellationToken);
        }
    }

    public Task NotificationAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString()).SendAsync(HubMethods.NotificationReceived, notification, cancellationToken);
}
