using Helpdesk.Application.Security;
using Helpdesk.Application.Tickets;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Tickets;
using static Helpdesk.UnitTests.TestData;

namespace Helpdesk.UnitTests.Application;

public sealed class TicketAccessPolicyTests
{
    private static readonly DateTimeOffset Created = Local(2026, 10, 5, 10, 0);

    private static AccessContext Agent(params Guid[] teams) => new(AgentUser, ActorRole.Agent, null, teams.ToHashSet());

    private static AccessContext OtherAgent(params Guid[] teams) => new(TestData.OtherAgent, ActorRole.Agent, null, teams.ToHashSet());

    private static AccessContext CustomerUserContext(Guid? customerId = null) => new(CustomerUser, ActorRole.Customer, customerId ?? Customer, new HashSet<Guid>());

    private static AccessContext Supervisor() => new(Guid.CreateVersion7(), ActorRole.Supervisor, null, new HashSet<Guid>());

    [Fact]
    public void Supervisors_see_everything_agents_see_their_queue_and_customers_their_own_tickets()
    {
        var ticket = Ticket.Open(NewTicket(), P2Terms(), Created);

        TicketAccessPolicy.CanView(ticket, Supervisor()).Should().BeTrue();
        TicketAccessPolicy.CanView(ticket, Agent(TeamA)).Should().BeTrue();
        TicketAccessPolicy.CanView(ticket, Agent(TeamB)).Should().BeFalse();
        TicketAccessPolicy.CanView(ticket, CustomerUserContext()).Should().BeTrue();
        TicketAccessPolicy.CanView(ticket, CustomerUserContext(Guid.CreateVersion7())).Should().BeFalse();
        TicketAccessPolicy.CanView(ticket, new AccessContext(Guid.CreateVersion7(), ActorRole.Integration, null, new HashSet<Guid>())).Should().BeFalse();
    }

    [Fact]
    public void Assignee_from_another_queue_still_sees_and_works_the_ticket()
    {
        var ticket = Ticket.Open(NewTicket(), P2Terms(), Created);
        ticket.Assign(TestData.OtherAgent, new Actor(Guid.CreateVersion7(), ActorRole.Supervisor), Created);

        var outsider = OtherAgent(TeamB);

        TicketAccessPolicy.CanView(ticket, outsider).Should().BeTrue();
        TicketAccessPolicy.CanWork(ticket, outsider).Should().BeTrue();
        TicketAccessPolicy.CanWork(ticket, Agent(TeamA)).Should().BeFalse("solo el asignado trabaja el ticket");
        TicketAccessPolicy.CanManage(ticket, Agent(TeamA)).Should().BeTrue("la cola puede reasignar");
    }

    [Fact]
    public void Allowed_actions_for_the_queue_on_a_new_ticket()
    {
        var ticket = Ticket.Open(NewTicket(), P2Terms(), Created);

        TicketAccessPolicy.AllowedActions(ticket, Agent(TeamA)).Should().BeEquivalentTo(
            [TicketAction.Assign, TicketAction.Transfer, TicketAction.Classify, TicketAction.ReviewSuggestion, TicketAction.Comment, TicketAction.Upload, TicketAction.InternalComment]);
    }

    [Fact]
    public void Allowed_actions_for_the_customer_on_a_resolved_ticket_include_survey()
    {
        var ticket = Ticket.Open(NewTicket(), P2Terms(), Created);
        ticket.Assign(AgentUser, new Actor(Guid.CreateVersion7(), ActorRole.Supervisor), Created);
        ticket.Start(new Actor(AgentUser, ActorRole.Agent), P2Terms(), Created.AddMinutes(10));
        ticket.Resolve("Listo", new Actor(AgentUser, ActorRole.Agent), P2Terms(), Created.AddDays(3), Created.AddHours(1));

        TicketAccessPolicy.AllowedActions(ticket, CustomerUserContext()).Should().BeEquivalentTo(
            [TicketAction.Reopen, TicketAction.Close, TicketAction.Comment, TicketAction.Upload, TicketAction.Survey]);
    }

    [Fact]
    public void Nobody_outside_sees_any_action()
    {
        var ticket = Ticket.Open(NewTicket(), P2Terms(), Created);

        TicketAccessPolicy.AllowedActions(ticket, Agent(TeamB)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(TransitionAction.Start, TicketTrigger.Start)]
    [InlineData(TransitionAction.Hold, TicketTrigger.Hold)]
    [InlineData(TransitionAction.Resume, TicketTrigger.Resume)]
    [InlineData(TransitionAction.Resolve, TicketTrigger.Resolve)]
    [InlineData(TransitionAction.Reopen, TicketTrigger.Reopen)]
    [InlineData(TransitionAction.Close, TicketTrigger.Close)]
    [InlineData(TransitionAction.Cancel, TicketTrigger.Cancel)]
    public void Transition_actions_map_to_triggers(TransitionAction action, TicketTrigger trigger) =>
        TicketAccessPolicy.ToTrigger(action).Should().Be(trigger);
}

public sealed class SlaStateCalculatorTests
{
    private static readonly DateTimeOffset Created = Local(2026, 10, 5, 10, 0);

    [Fact]
    public void New_ticket_is_on_track_then_at_risk_then_breached()
    {
        var ticket = Ticket.Open(NewTicket(), P2Terms(), Created);

        SlaStateCalculator.Calculate(ticket, Local(2026, 10, 5, 10, 10)).Should().Be(SlaState.OnTrack);
        SlaStateCalculator.Calculate(ticket, Local(2026, 10, 5, 10, 50)).Should().Be(SlaState.AtRisk);
        SlaStateCalculator.Calculate(ticket, Local(2026, 10, 5, 11, 5)).Should().Be(SlaState.Breached);
    }

    [Fact]
    public void Paused_and_finished_states()
    {
        var agent = new Actor(AgentUser, ActorRole.Agent);
        var ticket = Ticket.Open(NewTicket(), P2Terms(), Created);
        ticket.Assign(AgentUser, new Actor(Guid.CreateVersion7(), ActorRole.Supervisor), Created);
        ticket.Start(agent, P2Terms(), Created.AddMinutes(10));
        ticket.Hold("Esperando datos", agent, Created.AddMinutes(20));
        SlaStateCalculator.Calculate(ticket, Local(2026, 10, 9, 10, 0)).Should().Be(SlaState.Paused);

        ticket.Resume(agent, P2Terms(), Created.AddMinutes(40));
        ticket.Resolve("Listo", agent, P2Terms(), Created.AddDays(3), Created.AddMinutes(60));
        SlaStateCalculator.Calculate(ticket, Local(2026, 10, 9, 10, 0)).Should().Be(SlaState.Met);
    }

    [Fact]
    public void Cancelled_tickets_are_not_applicable_and_late_resolutions_are_breached()
    {
        var cancelled = Ticket.Open(NewTicket(), P2Terms(), Created);
        cancelled.Cancel("Duplicado", new Actor(Guid.CreateVersion7(), ActorRole.Supervisor), Created.AddMinutes(1));
        SlaStateCalculator.Calculate(cancelled, Created.AddDays(5)).Should().Be(SlaState.NotApplicable);

        SlaStateCalculator.Calculate(TicketStatus.Closed, Created, Created, Created, false, Created, Created, true, Created).Should().Be(SlaState.Breached);
    }
}
