using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Application.Security;

/// <summary>Identidad y pertenencia a colas con la que se decide el acceso a cada ticket.</summary>
public sealed record AccessContext(Guid UserId, ActorRole Role, Guid? CustomerId, IReadOnlySet<Guid> TeamIds)
{
    public bool IsStaff => Role is ActorRole.Admin or ActorRole.Supervisor or ActorRole.Agent;

    public bool SeesEverything => Role is ActorRole.Admin or ActorRole.Supervisor;

    public Actor Actor => new(UserId, Role);
}

/// <summary>
/// Autorización a nivel de objeto (OWASP API1 BOLA). Las políticas de los endpoints filtran por rol;
/// esta clase decide sobre <em>cada</em> ticket:
/// supervisores y administradores ven todo, un agente ve su cola y lo que tiene asignado, y un cliente
/// solo los tickets de su propio cliente.
/// </summary>
public static class TicketAccessPolicy
{
    public static bool CanView(Ticket ticket, AccessContext user)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(user);
        return user.Role switch
        {
            ActorRole.Admin or ActorRole.Supervisor => true,
            ActorRole.Agent => user.TeamIds.Contains(ticket.TeamId) || ticket.AssigneeId == user.UserId,
            ActorRole.Customer => user.CustomerId is { } customerId && ticket.CustomerId == customerId,
            _ => false,
        };
    }

    /// <summary>Gestionar: asignar, transferir, clasificar y revisar sugerencias (miembros de la cola o el asignado).</summary>
    public static bool CanManage(Ticket ticket, AccessContext user) =>
        user.SeesEverything || (user.Role == ActorRole.Agent && (user.TeamIds.Contains(ticket.TeamId) || ticket.AssigneeId == user.UserId));

    /// <summary>Trabajar: iniciar, poner en espera, reanudar y resolver (solo el asignado, o un supervisor).</summary>
    public static bool CanWork(Ticket ticket, AccessContext user) =>
        user.SeesEverything || (user.Role == ActorRole.Agent && ticket.AssigneeId == user.UserId);

    public static bool CanSeeInternal(AccessContext user) => user.IsStaff;

    /// <summary>Acciones disponibles para el usuario: estado (máquina de estados) + rol + relación con el ticket.</summary>
    public static IReadOnlyList<TicketAction> AllowedActions(Ticket ticket, AccessContext user)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!CanView(ticket, user))
        {
            return [];
        }

        var actions = new List<TicketAction>();
        foreach (var trigger in ticket.AvailableTriggers(user.Role))
        {
            var allowed = trigger switch
            {
                TicketTrigger.Assign or TicketTrigger.Transfer => CanManage(ticket, user),
                TicketTrigger.Start or TicketTrigger.Hold or TicketTrigger.Resume or TicketTrigger.Resolve => CanWork(ticket, user),
                TicketTrigger.Reopen => user.Role == ActorRole.Customer || CanWork(ticket, user),
                _ => true,
            };

            if (allowed)
            {
                actions.Add(Map(trigger));
            }
        }

        var open = !ticket.Status.IsTerminal();
        if (open && user.IsStaff && CanManage(ticket, user) && ticket.Status != TicketStatus.Resolved)
        {
            actions.Add(TicketAction.Classify);
            actions.Add(TicketAction.ReviewSuggestion);
        }

        if (open)
        {
            actions.Add(TicketAction.Comment);
            actions.Add(TicketAction.Upload);
            if (user.IsStaff)
            {
                actions.Add(TicketAction.InternalComment);
            }
        }

        if (user.Role == ActorRole.Customer && ticket.Survey is null && ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            actions.Add(TicketAction.Survey);
        }

        return actions;
    }

    public static TicketTrigger ToTrigger(TransitionAction action) => action switch
    {
        TransitionAction.Start => TicketTrigger.Start,
        TransitionAction.Hold => TicketTrigger.Hold,
        TransitionAction.Resume => TicketTrigger.Resume,
        TransitionAction.Resolve => TicketTrigger.Resolve,
        TransitionAction.Reopen => TicketTrigger.Reopen,
        TransitionAction.Close => TicketTrigger.Close,
        TransitionAction.Cancel => TicketTrigger.Cancel,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static TicketAction Map(TicketTrigger trigger) => trigger switch
    {
        TicketTrigger.Assign => TicketAction.Assign,
        TicketTrigger.Start => TicketAction.Start,
        TicketTrigger.Hold => TicketAction.Hold,
        TicketTrigger.Resume => TicketAction.Resume,
        TicketTrigger.Resolve => TicketAction.Resolve,
        TicketTrigger.Reopen => TicketAction.Reopen,
        TicketTrigger.Close => TicketAction.Close,
        TicketTrigger.Cancel => TicketAction.Cancel,
        TicketTrigger.Transfer => TicketAction.Transfer,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
    };
}
