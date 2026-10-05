using Helpdesk.Domain.Common;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.UnitTests.Domain;

public sealed class TicketStateMachineTests
{
    public static TheoryData<TicketStatus, TicketTrigger, TicketStatus> ValidTransitions => new()
    {
        { TicketStatus.New, TicketTrigger.Assign, TicketStatus.Assigned },
        { TicketStatus.Assigned, TicketTrigger.Assign, TicketStatus.Assigned },
        { TicketStatus.InProgress, TicketTrigger.Assign, TicketStatus.InProgress },
        { TicketStatus.OnHold, TicketTrigger.Assign, TicketStatus.OnHold },
        { TicketStatus.Assigned, TicketTrigger.Start, TicketStatus.InProgress },
        { TicketStatus.InProgress, TicketTrigger.Hold, TicketStatus.OnHold },
        { TicketStatus.OnHold, TicketTrigger.Resume, TicketStatus.InProgress },
        { TicketStatus.InProgress, TicketTrigger.Resolve, TicketStatus.Resolved },
        { TicketStatus.Resolved, TicketTrigger.Reopen, TicketStatus.InProgress },
        { TicketStatus.Resolved, TicketTrigger.Close, TicketStatus.Closed },
        { TicketStatus.New, TicketTrigger.Cancel, TicketStatus.Cancelled },
        { TicketStatus.Assigned, TicketTrigger.Cancel, TicketStatus.Cancelled },
        { TicketStatus.InProgress, TicketTrigger.Cancel, TicketStatus.Cancelled },
        { TicketStatus.OnHold, TicketTrigger.Cancel, TicketStatus.Cancelled },
        { TicketStatus.New, TicketTrigger.Transfer, TicketStatus.New },
        { TicketStatus.Assigned, TicketTrigger.Transfer, TicketStatus.New },
        { TicketStatus.InProgress, TicketTrigger.Transfer, TicketStatus.New },
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void Fire_returns_the_target_state_for_every_valid_transition(TicketStatus from, TicketTrigger trigger, TicketStatus expected)
    {
        var role = trigger == TicketTrigger.Close ? ActorRole.Supervisor : ActorRole.Admin;

        TicketStateMachine.Fire(from, trigger, role).Should().Be(expected);
    }

    [Fact]
    public void Every_combination_outside_the_table_is_rejected()
    {
        var valid = ValidTransitions.Select(row => (row.Data.Item1, row.Data.Item2)).ToHashSet();
        var invalid = Enum.GetValues<TicketStatus>()
            .SelectMany(status => Enum.GetValues<TicketTrigger>().Select(trigger => (status, trigger)))
            .Where(pair => !valid.Contains(pair))
            .ToList();

        invalid.Should().NotBeEmpty();
        foreach (var (status, trigger) in invalid)
        {
            var act = () => TicketStateMachine.Fire(status, trigger, ActorRole.Admin);
            act.Should().Throw<InvalidTransitionException>($"{trigger} desde {status} no existe");
        }
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void Terminal_states_have_no_exits(TicketStatus terminal)
    {
        foreach (var role in Enum.GetValues<ActorRole>())
        {
            TicketStateMachine.AvailableTriggers(terminal, role).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(TicketTrigger.Assign)]
    [InlineData(TicketTrigger.Start)]
    [InlineData(TicketTrigger.Hold)]
    [InlineData(TicketTrigger.Resolve)]
    [InlineData(TicketTrigger.Transfer)]
    public void Customers_cannot_work_tickets(TicketTrigger trigger)
    {
        var from = trigger switch
        {
            TicketTrigger.Start => TicketStatus.Assigned,
            TicketTrigger.Hold or TicketTrigger.Resolve => TicketStatus.InProgress,
            _ => TicketStatus.New,
        };

        var act = () => TicketStateMachine.Fire(from, trigger, ActorRole.Customer);

        act.Should().Throw<ActionNotPermittedException>();
    }

    [Fact]
    public void Agents_cannot_close_tickets_because_closure_needs_customer_confirmation()
    {
        TicketStateMachine.IsPermitted(TicketStatus.Resolved, TicketTrigger.Close, ActorRole.Agent).Should().BeFalse();
        TicketStateMachine.IsPermitted(TicketStatus.Resolved, TicketTrigger.Close, ActorRole.Customer).Should().BeTrue();
        TicketStateMachine.IsPermitted(TicketStatus.Resolved, TicketTrigger.Close, ActorRole.System).Should().BeTrue();
    }

    [Theory]
    [InlineData(TicketStatus.New, true)]
    [InlineData(TicketStatus.Assigned, true)]
    [InlineData(TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.OnHold, false)]
    public void Customers_can_cancel_only_before_work_starts(TicketStatus from, bool expected) =>
        TicketStateMachine.IsPermitted(from, TicketTrigger.Cancel, ActorRole.Customer).Should().Be(expected);

    [Fact]
    public void Agents_cannot_cancel_but_supervisors_can()
    {
        TicketStateMachine.IsPermitted(TicketStatus.InProgress, TicketTrigger.Cancel, ActorRole.Agent).Should().BeFalse();
        TicketStateMachine.IsPermitted(TicketStatus.InProgress, TicketTrigger.Cancel, ActorRole.Supervisor).Should().BeTrue();
    }

    [Fact]
    public void Customer_can_reopen_a_resolved_ticket()
    {
        TicketStateMachine.Fire(TicketStatus.Resolved, TicketTrigger.Reopen, ActorRole.Customer).Should().Be(TicketStatus.InProgress);
    }

    [Fact]
    public void Only_the_system_or_staff_resume_tickets_on_hold()
    {
        TicketStateMachine.IsPermitted(TicketStatus.OnHold, TicketTrigger.Resume, ActorRole.System).Should().BeTrue();
        TicketStateMachine.IsPermitted(TicketStatus.OnHold, TicketTrigger.Resume, ActorRole.Customer).Should().BeFalse();
    }

    [Fact]
    public void Available_triggers_for_an_agent_in_progress()
    {
        TicketStateMachine.AvailableTriggers(TicketStatus.InProgress, ActorRole.Agent)
            .Should().BeEquivalentTo([TicketTrigger.Assign, TicketTrigger.Hold, TicketTrigger.Resolve, TicketTrigger.Transfer]);
    }

    [Fact]
    public void Available_triggers_for_a_customer_on_a_resolved_ticket()
    {
        TicketStateMachine.AvailableTriggers(TicketStatus.Resolved, ActorRole.Customer)
            .Should().BeEquivalentTo([TicketTrigger.Reopen, TicketTrigger.Close]);
    }

    [Fact]
    public void Integration_clients_cannot_change_status()
    {
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            TicketStateMachine.AvailableTriggers(status, ActorRole.Integration).Should().BeEmpty();
        }
    }
}
