using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Tickets;

/// <summary>
/// Transiciones permitidas y roles que pueden ejecutarlas (R5). Es la única fuente de verdad:
/// la API la usa para validar y para decirle al front qué botones mostrar.
/// </summary>
public static class TicketStateMachine
{
    private static readonly Dictionary<(TicketStatus From, TicketTrigger Trigger), TicketStatus> Transitions = new()
    {
        [(TicketStatus.New, TicketTrigger.Assign)] = TicketStatus.Assigned,
        [(TicketStatus.Assigned, TicketTrigger.Assign)] = TicketStatus.Assigned,
        [(TicketStatus.InProgress, TicketTrigger.Assign)] = TicketStatus.InProgress,
        [(TicketStatus.OnHold, TicketTrigger.Assign)] = TicketStatus.OnHold,
        [(TicketStatus.Assigned, TicketTrigger.Start)] = TicketStatus.InProgress,
        [(TicketStatus.InProgress, TicketTrigger.Hold)] = TicketStatus.OnHold,
        [(TicketStatus.OnHold, TicketTrigger.Resume)] = TicketStatus.InProgress,
        [(TicketStatus.InProgress, TicketTrigger.Resolve)] = TicketStatus.Resolved,
        [(TicketStatus.Resolved, TicketTrigger.Reopen)] = TicketStatus.InProgress,
        [(TicketStatus.Resolved, TicketTrigger.Close)] = TicketStatus.Closed,
        [(TicketStatus.New, TicketTrigger.Cancel)] = TicketStatus.Cancelled,
        [(TicketStatus.Assigned, TicketTrigger.Cancel)] = TicketStatus.Cancelled,
        [(TicketStatus.InProgress, TicketTrigger.Cancel)] = TicketStatus.Cancelled,
        [(TicketStatus.OnHold, TicketTrigger.Cancel)] = TicketStatus.Cancelled,
        [(TicketStatus.New, TicketTrigger.Transfer)] = TicketStatus.New,
        [(TicketStatus.Assigned, TicketTrigger.Transfer)] = TicketStatus.New,
        [(TicketStatus.InProgress, TicketTrigger.Transfer)] = TicketStatus.New,
    };

    private static readonly ActorRole[] Staff = [ActorRole.Admin, ActorRole.Supervisor, ActorRole.Agent];

    private static readonly Dictionary<TicketTrigger, ActorRole[]> AllowedRoles = new()
    {
        [TicketTrigger.Assign] = Staff,
        [TicketTrigger.Start] = Staff,
        [TicketTrigger.Hold] = Staff,
        [TicketTrigger.Resume] = [.. Staff, ActorRole.System],
        [TicketTrigger.Resolve] = Staff,
        [TicketTrigger.Reopen] = [.. Staff, ActorRole.Customer],
        [TicketTrigger.Close] = [ActorRole.Admin, ActorRole.Supervisor, ActorRole.Customer, ActorRole.System],
        [TicketTrigger.Cancel] = [ActorRole.Admin, ActorRole.Supervisor, ActorRole.Customer],
        [TicketTrigger.Transfer] = Staff,
    };

    /// <summary>Estados desde los que el cliente puede cancelar su propio ticket (antes de que se trabaje).</summary>
    private static readonly TicketStatus[] CustomerCancellable = [TicketStatus.New, TicketStatus.Assigned];

    public static bool CanFire(TicketStatus from, TicketTrigger trigger) => Transitions.ContainsKey((from, trigger));

    public static bool IsPermitted(TicketStatus from, TicketTrigger trigger, ActorRole role)
    {
        if (!CanFire(from, trigger) || !AllowedRoles[trigger].Contains(role))
        {
            return false;
        }

        return trigger != TicketTrigger.Cancel || role != ActorRole.Customer || CustomerCancellable.Contains(from);
    }

    /// <summary>Estado destino; lanza si la transición no existe o el rol no puede ejecutarla.</summary>
    public static TicketStatus Fire(TicketStatus from, TicketTrigger trigger, ActorRole role)
    {
        if (!Transitions.TryGetValue((from, trigger), out var target))
        {
            throw new InvalidTransitionException(from.ToString(), trigger.ToString());
        }

        if (!IsPermitted(from, trigger, role))
        {
            throw new ActionNotPermittedException(trigger.ToString(), role.ToString());
        }

        return target;
    }

    public static IReadOnlyList<TicketTrigger> AvailableTriggers(TicketStatus from, ActorRole role) =>
        [.. Enum.GetValues<TicketTrigger>().Where(trigger => IsPermitted(from, trigger, role))];
}
