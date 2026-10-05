using Helpdesk.Domain.Common;
using Helpdesk.Domain.Tickets;
using static Helpdesk.UnitTests.TestData;

namespace Helpdesk.UnitTests.Domain;

/// <summary>
/// Agregado Ticket con regla P2 (respuesta 60 min, resolución 480 min hábiles, aviso al 75 %) creado el
/// lunes 5-oct-2026 a las 10:00 (hora de Ecuador).
/// </summary>
public sealed class TicketTests
{
    private static readonly DateTimeOffset Created = Local(2026, 10, 5, 10, 0);
    private static readonly Actor Agent = new(AgentUser, ActorRole.Agent);
    private static readonly Actor CustomerActor = new(CustomerUser, ActorRole.Customer);
    private static readonly Actor Supervisor = new(Guid.CreateVersion7(), ActorRole.Supervisor);

    private readonly SlaTermsFixture _sla = new();

    [Fact]
    public void Open_calculates_priority_and_sla_targets()
    {
        var ticket = NewOpenTicket();

        ticket.Status.Should().Be(TicketStatus.New);
        ticket.Priority.Should().Be(Priority.High);
        ticket.SlaPolicyId.Should().Be(PolicyP2);
        ticket.FirstResponseDueAt.Should().Be(Local(2026, 10, 5, 11, 0));
        ticket.FirstResponseWarningAt.Should().Be(Local(2026, 10, 5, 10, 45));
        ticket.ResolutionDueAt.Should().Be(Local(2026, 10, 6, 10, 0));
        ticket.ResolutionWarningAt.Should().Be(Local(2026, 10, 5, 17, 0));
        ticket.History.Should().ContainSingle(h => h.FromStatus == null && h.ToStatus == TicketStatus.New);
        ticket.DomainEvents.Should().ContainSingle(e => e is TicketCreated);
    }

    [Fact]
    public void Open_trims_and_validates_title()
    {
        var blank = () => Ticket.Open(NewTicket() with { Title = "   " }, _sla.P2, Created);
        var tooLong = () => Ticket.Open(NewTicket() with { Title = new string('x', Ticket.TitleMaxLength + 1) }, _sla.P2, Created);

        blank.Should().Throw<DomainException>().Which.Code.Should().Be("title_required");
        tooLong.Should().Throw<DomainException>().Which.Code.Should().Be("title_too_long");
        Ticket.Open(NewTicket() with { Title = "  Error en app  " }, _sla.P2, Created).Title.Should().Be("Error en app");
    }

    [Fact]
    public void Assign_moves_new_ticket_to_assigned_and_raises_event()
    {
        var ticket = NewOpenTicket();

        ticket.Assign(AgentUser, Supervisor, Created.AddMinutes(5));

        ticket.Status.Should().Be(TicketStatus.Assigned);
        ticket.AssigneeId.Should().Be(AgentUser);
        ticket.History.Should().HaveCount(2);
        ticket.DomainEvents.OfType<TicketAssigned>().Should().ContainSingle(e => e.AssigneeId == AgentUser && e.PreviousAssigneeId == null);
    }

    [Fact]
    public void Reassigning_keeps_the_status_and_rejects_the_same_person()
    {
        var ticket = NewOpenTicket();
        ticket.Assign(AgentUser, Supervisor, Created);

        ticket.Assign(OtherAgent, Supervisor, Created.AddMinutes(1));
        var same = () => ticket.Assign(OtherAgent, Supervisor, Created.AddMinutes(2));

        ticket.Status.Should().Be(TicketStatus.Assigned);
        ticket.History.Should().HaveCount(2, "reasignar no cambia el estado");
        same.Should().Throw<DomainException>().Which.Code.Should().Be("already_assigned");
    }

    [Fact]
    public void Customers_cannot_assign()
    {
        var act = () => NewOpenTicket().Assign(AgentUser, CustomerActor, Created);

        act.Should().Throw<ActionNotPermittedException>();
    }

    [Fact]
    public void Start_registers_the_first_response_in_business_minutes()
    {
        var ticket = AssignedTicket();

        ticket.Start(Agent, _sla.P2, Local(2026, 10, 5, 10, 30));

        ticket.Status.Should().Be(TicketStatus.InProgress);
        ticket.FirstRespondedAt.Should().Be(Local(2026, 10, 5, 10, 30));
        ticket.FirstResponseBusinessMinutes.Should().Be(30);
        ticket.FirstResponseBreached.Should().BeFalse();
    }

    [Fact]
    public void A_late_start_breaches_the_first_response_target()
    {
        var ticket = AssignedTicket();

        ticket.Start(Agent, _sla.P2, Local(2026, 10, 5, 11, 30));

        ticket.FirstResponseBreached.Should().BeTrue();
    }

    [Fact]
    public void Hold_requires_a_reason()
    {
        var ticket = InProgressTicket();

        var act = () => ticket.Hold("  ", Agent, Local(2026, 10, 5, 11, 0));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("reason_required");
    }

    [Fact]
    public void Time_on_hold_is_added_to_the_resolution_target()
    {
        var ticket = InProgressTicket();
        ticket.Hold("Esperando captura del cliente", Agent, Local(2026, 10, 5, 11, 0));

        ticket.Resume(Agent, _sla.P2, Local(2026, 10, 6, 11, 0));

        ticket.Status.Should().Be(TicketStatus.InProgress);
        ticket.SlaPausedAt.Should().BeNull();
        ticket.SlaPausedBusinessMinutes.Should().Be(480);
        ticket.ResolutionDueAt.Should().Be(Local(2026, 10, 7, 10, 0));
    }

    [Fact]
    public void A_ticket_on_hold_never_breaches_resolution()
    {
        var ticket = InProgressTicket();
        ticket.Hold("Esperando al proveedor", Agent, Local(2026, 10, 5, 11, 0));
        ticket.ClearDomainEvents();

        var changed = ticket.EvaluateSla(Local(2026, 10, 9, 12, 0));

        changed.Should().BeFalse();
        ticket.ResolutionBreached.Should().BeFalse();
    }

    [Fact]
    public void Resolve_stops_the_clock_and_measures_net_business_minutes()
    {
        var ticket = InProgressTicket();
        var autoClose = Local(2026, 10, 9, 9, 0);

        ticket.Resolve("Se restableció el acceso", Agent, _sla.P2, autoClose, Local(2026, 10, 6, 9, 0));

        ticket.Status.Should().Be(TicketStatus.Resolved);
        ticket.ResolvedAt.Should().Be(Local(2026, 10, 6, 9, 0));
        ticket.ResolutionBreached.Should().BeFalse();
        ticket.ResolutionBusinessMinutes.Should().Be(420);
        ticket.SlaPausedAt.Should().Be(Local(2026, 10, 6, 9, 0));
        ticket.AutoCloseAt.Should().Be(autoClose);
        ticket.ResolutionSummary.Should().Be("Se restableció el acceso");
        ticket.History.Last().Reason.Should().Be("Se restableció el acceso");
    }

    [Fact]
    public void A_late_resolution_is_marked_as_breached()
    {
        var ticket = InProgressTicket();

        ticket.Resolve("Listo", Agent, _sla.P2, Local(2026, 10, 9, 9, 0), Local(2026, 10, 6, 11, 0));

        ticket.ResolutionBreached.Should().BeTrue();
    }

    [Fact]
    public void Reopening_counts_the_resolved_period_as_paused_time()
    {
        var ticket = ResolvedTicket(Local(2026, 10, 6, 9, 0));

        ticket.Reopen("El error volvió a aparecer", CustomerActor, _sla.P2, Local(2026, 10, 6, 10, 0));

        ticket.Status.Should().Be(TicketStatus.InProgress);
        ticket.ReopenCount.Should().Be(1);
        ticket.ResolvedAt.Should().BeNull();
        ticket.AutoCloseAt.Should().BeNull();
        ticket.ResolutionSummary.Should().BeNull();
        ticket.SlaPausedBusinessMinutes.Should().Be(60);
        ticket.ResolutionDueAt.Should().Be(Local(2026, 10, 6, 11, 0));
    }

    [Fact]
    public void Customer_closes_a_resolved_ticket_but_an_agent_cannot()
    {
        var ticket = ResolvedTicket(Local(2026, 10, 6, 9, 0));
        var byAgent = () => ticket.Close(Agent, Local(2026, 10, 6, 9, 30));

        byAgent.Should().Throw<ActionNotPermittedException>();
        ticket.Close(CustomerActor, Local(2026, 10, 6, 9, 30));

        ticket.Status.Should().Be(TicketStatus.Closed);
        ticket.ClosedAt.Should().Be(Local(2026, 10, 6, 9, 30));
    }

    [Fact]
    public void Customer_can_cancel_a_new_ticket_with_a_reason()
    {
        var ticket = NewOpenTicket();

        ticket.Cancel("Ya lo resolví por mi cuenta", CustomerActor, Created.AddMinutes(3));

        ticket.Status.Should().Be(TicketStatus.Cancelled);
        ticket.ClosedAt.Should().NotBeNull();
        ticket.History.Last().Reason.Should().Be("Ya lo resolví por mi cuenta");
    }

    [Fact]
    public void Customer_cannot_cancel_once_work_started()
    {
        var act = () => InProgressTicket().Cancel("Ya no lo necesito", CustomerActor, Local(2026, 10, 5, 12, 0));

        act.Should().Throw<ActionNotPermittedException>();
    }

    [Fact]
    public void Transfer_requeues_the_ticket_in_the_new_team()
    {
        var ticket = AssignedTicket();

        ticket.Transfer(TeamB, "Corresponde a fraude", Agent, Created.AddMinutes(20));

        ticket.Status.Should().Be(TicketStatus.New);
        ticket.TeamId.Should().Be(TeamB);
        ticket.AssigneeId.Should().BeNull();
        ticket.History.Last().Should().Match<TicketStatusChange>(h => h.FromStatus == TicketStatus.Assigned && h.ToStatus == TicketStatus.New && h.Reason == "Corresponde a fraude");
        ticket.DomainEvents.OfType<TicketTransferred>().Should().ContainSingle(e => e.FromTeamId == TeamA && e.ToTeamId == TeamB);
    }

    [Fact]
    public void Transfer_of_a_new_ticket_is_recorded_and_rejects_the_same_team()
    {
        var ticket = NewOpenTicket();

        ticket.Transfer(TeamB, "Cola equivocada", Agent, Created.AddMinutes(1));
        var same = () => ticket.Transfer(TeamB, "Otra vez", Agent, Created.AddMinutes(2));

        ticket.History.Should().HaveCount(2, "la transferencia se registra aunque el estado siga siendo Nuevo");
        same.Should().Throw<DomainException>().Which.Code.Should().Be("same_team");
    }

    [Fact]
    public void First_public_staff_comment_counts_as_first_response()
    {
        var ticket = NewOpenTicket();

        ticket.AddComment(AgentUser, ActorRole.Agent, "Nota interna", isInternal: true, _sla.P2, Created.AddMinutes(10));
        ticket.FirstRespondedAt.Should().BeNull("un comentario interno no responde al cliente");

        ticket.AddComment(AgentUser, ActorRole.Agent, "Estamos revisando su caso", isInternal: false, _sla.P2, Created.AddMinutes(20));

        ticket.FirstRespondedAt.Should().Be(Created.AddMinutes(20));
        ticket.Comments.Should().HaveCount(2);
        ticket.DomainEvents.OfType<TicketCommentAdded>().Should().HaveCount(2);
    }

    [Fact]
    public void Customers_cannot_write_internal_comments()
    {
        var act = () => NewOpenTicket().AddComment(CustomerUser, ActorRole.Customer, "hola", isInternal: true, _sla.P2, Created);

        act.Should().Throw<ActionNotPermittedException>();
    }

    [Fact]
    public void A_customer_reply_resumes_a_ticket_on_hold()
    {
        var ticket = InProgressTicket();
        ticket.Hold("Esperando datos", Agent, Local(2026, 10, 5, 11, 0));

        ticket.AddComment(CustomerUser, ActorRole.Customer, "Adjunto la captura", isInternal: false, _sla.P2, Local(2026, 10, 5, 12, 0));

        ticket.Status.Should().Be(TicketStatus.InProgress);
        ticket.SlaPausedBusinessMinutes.Should().Be(60);
        ticket.History.Last().ChangedById.Should().BeNull("lo reanudó el sistema");
    }

    [Fact]
    public void Closed_tickets_do_not_accept_comments()
    {
        var ticket = ResolvedTicket(Local(2026, 10, 6, 9, 0));
        ticket.Close(CustomerActor, Local(2026, 10, 6, 9, 30));

        var act = () => ticket.AddComment(CustomerUser, ActorRole.Customer, "gracias", isInternal: false, _sla.P2, Local(2026, 10, 6, 10, 0));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("ticket_closed");
    }

    [Fact]
    public void Evaluate_sla_raises_each_warning_and_breach_once()
    {
        var ticket = NewOpenTicket();
        ticket.ClearDomainEvents();

        ticket.EvaluateSla(Local(2026, 10, 5, 10, 46)).Should().BeTrue();
        ticket.ResponseWarningSent.Should().BeTrue();
        ticket.EscalationLevel.Should().Be(1);
        ticket.EvaluateSla(Local(2026, 10, 5, 10, 50)).Should().BeFalse();

        ticket.EvaluateSla(Local(2026, 10, 5, 11, 1)).Should().BeTrue();
        ticket.FirstResponseBreached.Should().BeTrue();
        ticket.EscalationLevel.Should().Be(2);

        ticket.EvaluateSla(Local(2026, 10, 5, 17, 0)).Should().BeTrue();
        ticket.ResolutionWarningSent.Should().BeTrue();

        ticket.EvaluateSla(Local(2026, 10, 6, 10, 1)).Should().BeTrue();
        ticket.ResolutionBreached.Should().BeTrue();

        ticket.DomainEvents.OfType<SlaWarningRaised>().Select(e => e.Metric).Should().Equal(SlaMetric.FirstResponse, SlaMetric.Resolution);
        ticket.DomainEvents.OfType<SlaBreached>().Select(e => e.Metric).Should().Equal(SlaMetric.FirstResponse, SlaMetric.Resolution);
    }

    [Fact]
    public void Evaluate_sla_ignores_terminal_tickets()
    {
        var ticket = NewOpenTicket();
        ticket.Cancel("Duplicado", Supervisor, Created.AddMinutes(1));

        ticket.EvaluateSla(Local(2026, 10, 20, 10, 0)).Should().BeFalse();
    }

    [Fact]
    public void Resolved_tickets_close_automatically_after_the_deadline()
    {
        var ticket = ResolvedTicket(Local(2026, 10, 6, 9, 0), autoCloseAt: Local(2026, 10, 9, 9, 0));

        ticket.TryAutoClose(Local(2026, 10, 8, 9, 0)).Should().BeFalse();
        ticket.TryAutoClose(Local(2026, 10, 9, 9, 1)).Should().BeTrue();

        ticket.Status.Should().Be(TicketStatus.Closed);
        ticket.History.Last().Reason.Should().StartWith("Cierre automático");
        ticket.TryAutoClose(Local(2026, 10, 9, 10, 0)).Should().BeFalse();
    }

    [Fact]
    public void Survey_on_a_resolved_ticket_confirms_and_closes_it()
    {
        var ticket = ResolvedTicket(Local(2026, 10, 6, 9, 0));

        var survey = ticket.SubmitSurvey(CustomerUser, 5, "  Muy rápido  ", Local(2026, 10, 6, 10, 0));

        ticket.Status.Should().Be(TicketStatus.Closed);
        survey.IsSatisfied.Should().BeTrue();
        survey.Comment.Should().Be("Muy rápido");
        var again = () => ticket.SubmitSurvey(CustomerUser, 4, null, Local(2026, 10, 6, 11, 0));
        again.Should().Throw<DomainException>().Which.Code.Should().Be("survey_exists");
    }

    [Fact]
    public void Survey_validates_status_and_rating()
    {
        var open = () => InProgressTicket().SubmitSurvey(CustomerUser, 5, null, Local(2026, 10, 6, 10, 0));
        var outOfRange = () => ResolvedTicket(Local(2026, 10, 6, 9, 0)).SubmitSurvey(CustomerUser, 6, null, Local(2026, 10, 6, 10, 0));

        open.Should().Throw<DomainException>().Which.Code.Should().Be("survey_not_allowed");
        outOfRange.Should().Throw<DomainException>().Which.Code.Should().Be("invalid_rating");
    }

    [Fact]
    public void Reclassify_to_critical_applies_the_24x7_policy()
    {
        var ticket = NewOpenTicket();

        ticket.Reclassify(Guid.CreateVersion7(), Impact.High, Urgency.High, _sla.P1, Agent, Created.AddMinutes(10));

        ticket.Priority.Should().Be(Priority.Critical);
        ticket.SlaPolicyId.Should().Be(PolicyP1);
        ticket.FirstResponseDueAt.Should().Be(Local(2026, 10, 5, 10, 30));
        ticket.ResolutionDueAt.Should().Be(Local(2026, 10, 5, 14, 0));
        ticket.DomainEvents.OfType<TicketReclassified>().Should().ContainSingle();
    }

    [Fact]
    public void Reclassify_rejects_customers_and_resolved_tickets()
    {
        var byCustomer = () => NewOpenTicket().Reclassify(null, Impact.Low, Urgency.Low, _sla.P2, CustomerActor, Created);
        var resolved = () => ResolvedTicket(Local(2026, 10, 6, 9, 0)).Reclassify(null, Impact.Low, Urgency.Low, _sla.P2, Agent, Local(2026, 10, 6, 10, 0));

        byCustomer.Should().Throw<ActionNotPermittedException>();
        resolved.Should().Throw<DomainException>().Which.Code.Should().Be("ticket_not_editable");
    }

    [Fact]
    public void Attachments_respect_visibility_and_limit()
    {
        var ticket = NewOpenTicket();
        var internalByCustomer = () => ticket.AddAttachment(Attachment(ticket.Id, isInternal: true), ActorRole.Customer);
        internalByCustomer.Should().Throw<ActionNotPermittedException>();

        for (var i = 0; i < Helpdesk.Domain.Attachments.AttachmentRules.MaxPerTicket; i++)
        {
            ticket.AddAttachment(Attachment(ticket.Id, isInternal: false), ActorRole.Customer);
        }

        var overLimit = () => ticket.AddAttachment(Attachment(ticket.Id, isInternal: false), ActorRole.Agent);
        overLimit.Should().Throw<DomainException>().Which.Code.Should().Be("attachment_limit");
    }

    [Fact]
    public void Available_triggers_depend_on_role()
    {
        var ticket = InProgressTicket();

        ticket.AvailableTriggers(ActorRole.Customer).Should().BeEmpty();
        ticket.AvailableTriggers(ActorRole.Supervisor).Should().Contain(TicketTrigger.Cancel);
    }

    [Fact]
    public void Display_number_is_zero_padded() => Ticket.FormatNumber(123).Should().Be("HD-000123");

    private Ticket NewOpenTicket() => Ticket.Open(NewTicket(), _sla.P2, Created);

    private Ticket AssignedTicket()
    {
        var ticket = NewOpenTicket();
        ticket.Assign(AgentUser, Supervisor, Created.AddMinutes(5));
        return ticket;
    }

    private Ticket InProgressTicket()
    {
        var ticket = AssignedTicket();
        ticket.Start(Agent, _sla.P2, Local(2026, 10, 5, 10, 30));
        return ticket;
    }

    private Ticket ResolvedTicket(DateTimeOffset resolvedAt, DateTimeOffset? autoCloseAt = null)
    {
        var ticket = InProgressTicket();
        ticket.Resolve("Se restableció el acceso", Agent, _sla.P2, autoCloseAt ?? resolvedAt.AddDays(3), resolvedAt);
        return ticket;
    }

    private static TicketAttachment Attachment(Guid ticketId, bool isInternal) =>
        new(ticketId, null, AgentUser, "captura.png", "image/png", 1024, new string('a', 64), Guid.CreateVersion7().ToString("N"), isInternal, Created);

    private sealed class SlaTermsFixture
    {
        public Helpdesk.Domain.Sla.SlaTerms P2 { get; } = P2Terms();

        public Helpdesk.Domain.Sla.SlaTerms P1 { get; } = P1Terms();
    }
}
