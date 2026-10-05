using Helpdesk.Api.Security;
using Helpdesk.Application.Abstractions;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Domain.Common;
using Helpdesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Helpdesk.Api.Realtime;

/// <summary>
/// Hub de solo servidor → cliente. Al conectar, cada usuario entra a los grupos que le corresponden:
/// sus colas (agentes), "supervisors" (supervisión y administración) o su cliente (portal).
/// </summary>
[Authorize(Policy = Policies.User)]
public sealed class NotificationsHub(IUserDirectory users) : Hub
{
    public const string Supervisors = "supervisors";

    public static string TeamGroup(Guid teamId) => $"team:{teamId}";

    public static string CustomerGroup(Guid customerId) => $"customer:{customerId}";

    public override async Task OnConnectedAsync()
    {
        // Se leen los claims de la conexión (no IHttpContextAccessor: con long polling o WebSockets no siempre existe).
        var user = Context.User;
        var role = Enum.TryParse<ActorRole>(user?.FindFirst(HelpdeskClaims.Role)?.Value, out var parsed) ? parsed : ActorRole.System;
        var userId = Guid.TryParse(user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : Guid.Empty;

        if (role is ActorRole.Admin or ActorRole.Supervisor)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, Supervisors);
        }

        if (role is ActorRole.Admin or ActorRole.Supervisor or ActorRole.Agent)
        {
            foreach (var teamId in await users.GetTeamIdsAsync(userId, Context.ConnectionAborted))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));
            }
        }

        if (role == ActorRole.Customer && Guid.TryParse(user?.FindFirst(HelpdeskClaims.CustomerId)?.Value, out var customerId))
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
