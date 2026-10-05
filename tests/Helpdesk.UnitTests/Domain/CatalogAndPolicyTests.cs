using Helpdesk.Domain.Catalog;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;
using static Helpdesk.UnitTests.TestData;

namespace Helpdesk.UnitTests.Domain;

public sealed class SlaPolicyTests
{
    private static readonly Guid Calendar = Guid.CreateVersion7();

    [Fact]
    public void Selector_prefers_the_policy_for_the_customer_tier()
    {
        var general = Policy(Priority.High, null, 60, 480);
        var premium = Policy(Priority.High, ServiceTier.Premium, 30, 240);

        SlaPolicySelector.Select([general, premium], Priority.High, ServiceTier.Premium).Should().BeSameAs(premium);
        SlaPolicySelector.Select([general, premium], Priority.High, ServiceTier.Standard).Should().BeSameAs(general);
    }

    [Fact]
    public void Selector_ignores_other_priorities_and_fails_when_nothing_matches()
    {
        var act = () => SlaPolicySelector.Select([Policy(Priority.Low, null, 480, 2400)], Priority.Critical, ServiceTier.Standard);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("sla_policy_missing");
    }

    [Theory]
    [InlineData(0, 480, 75)]
    [InlineData(60, 30, 75)]
    [InlineData(60, 480, 40)]
    [InlineData(60, 480, 99)]
    public void Targets_are_validated(int response, int resolution, int warning)
    {
        var act = () => Policy(Priority.High, null, response, resolution, warning);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("invalid_sla_target");
    }

    [Fact]
    public void Terms_compute_the_warning_minutes_rounding_up()
    {
        var terms = SlaTerms.From(Policy(Priority.High, null, 50, 480, 75), EcuadorOffice());

        terms.WarningMinutes(50).Should().Be(38);
        terms.WarningMinutes(480).Should().Be(360);
    }

    [Fact]
    public void Calendar_can_be_changed() =>
        Policy(Priority.High, null, 60, 480).Invoking(p => p.ChangeCalendar(Guid.CreateVersion7())).Should().NotThrow();

    private static SlaPolicy Policy(Priority priority, ServiceTier? tier, int response, int resolution, int warning = 75) =>
        new(Guid.CreateVersion7(), $"{priority} {tier}", priority, tier, response, resolution, warning, Calendar);
}

public sealed class CustomerTests
{
    [Fact]
    public void Anonymize_removes_contact_data_and_blocks_changes()
    {
        var customer = new Customer("CLI-00001", "Andrea Paredes", CustomerKind.Person, ServiceTier.Standard, "andrea@correo.example", "0990000001", Local(2026, 1, 1, 9));

        customer.Anonymize(Local(2026, 10, 5, 9));

        customer.DisplayName.Should().Be("Cliente anonimizado CLI-00001");
        customer.Email.Should().BeNull();
        customer.Phone.Should().BeNull();
        customer.IsActive.Should().BeFalse();
        customer.AnonymizedAt.Should().Be(Local(2026, 10, 5, 9));
        var update = () => customer.Update("Otra", CustomerKind.Person, ServiceTier.Premium, null, null);
        update.Should().Throw<DomainException>().Which.Code.Should().Be("customer_anonymized");
        var twice = () => customer.Anonymize(Local(2026, 10, 6, 9));
        twice.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_changes_contact_data_and_tier()
    {
        var customer = new Customer("CLI-00002", "Comercial Andes", CustomerKind.Organization, ServiceTier.Standard, null, null, Local(2026, 1, 1, 9));

        customer.Update("Comercial Andes S.A.", CustomerKind.Organization, ServiceTier.Premium, "soporte@andes.example", null);

        customer.ServiceTier.Should().Be(ServiceTier.Premium);
        customer.Email.Should().Be("soporte@andes.example");
    }
}

public sealed class TicketSuggestionTests
{
    private static readonly SuggestedClassification Classification =
        new("security_fraud", "fraud_security", Urgency.High, 0.91m, 0.95m, "auto_route", []);

    [Fact]
    public void Accept_records_the_final_values_and_reviewer()
    {
        var suggestion = NewSuggestion();
        var category = Guid.CreateVersion7();

        suggestion.Accept(AgentUser, category, TeamB, Urgency.High, Local(2026, 10, 5, 10, 5));

        suggestion.Status.Should().Be(SuggestionStatus.Accepted);
        suggestion.FinalCategoryId.Should().Be(category);
        suggestion.ReviewedById.Should().Be(AgentUser);
    }

    [Fact]
    public void A_reviewed_suggestion_cannot_be_reviewed_again()
    {
        var suggestion = NewSuggestion();
        suggestion.Dismiss(AgentUser, Local(2026, 10, 5, 10, 5));

        var act = () => suggestion.Correct(AgentUser, null, TeamA, Urgency.Low, Local(2026, 10, 5, 10, 6));

        suggestion.Status.Should().Be(SuggestionStatus.Dismissed);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("suggestion_reviewed");
    }

    [Fact]
    public void Correct_marks_the_suggestion_as_corrected()
    {
        var suggestion = NewSuggestion();

        suggestion.Correct(AgentUser, null, TeamA, Urgency.Low, Local(2026, 10, 5, 10, 5));

        suggestion.Status.Should().Be(SuggestionStatus.Corrected);
        suggestion.FinalUrgency.Should().Be(Urgency.Low);
    }

    [Fact]
    public void Confidence_must_be_a_probability()
    {
        var act = () => new TicketSuggestion(Guid.CreateVersion7(), SuggestionSource.Integration, "x", "1", null, Classification with { TeamConfidence = 1.5m }, Local(2026, 10, 5, 10));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("invalid_confidence");
    }

    [Fact]
    public void Codes_are_trimmed_and_limited()
    {
        var suggestion = new TicketSuggestion(
            Guid.CreateVersion7(),
            SuggestionSource.NlpApi,
            "nlp-tickets-es",
            "2026.09.25",
            null,
            Classification with { CategoryCode = "  " + new string('c', 60) + " ", TeamCode = "   " },
            Local(2026, 10, 5, 10));

        suggestion.SuggestedCategoryCode.Should().HaveLength(TicketSuggestion.CodeMaxLength);
        suggestion.SuggestedTeamCode.Should().BeNull();
    }

    private static TicketSuggestion NewSuggestion() =>
        new(Guid.CreateVersion7(), SuggestionSource.NlpApi, "nlp-tickets-es", "2026.09.25-7a9c6964", Guid.CreateVersion7(), Classification, Local(2026, 10, 5, 10));
}

public sealed class TeamAndNotificationTests
{
    [Fact]
    public void Adding_an_existing_member_updates_the_lead_flag()
    {
        var team = new Team(TeamA, "fraud_security", "Fraude y Seguridad", "Casos de fraude", Local(2026, 1, 1, 9));

        team.AddMember(AgentUser, isLead: false, Local(2026, 1, 1, 9));
        team.AddMember(AgentUser, isLead: true, Local(2026, 1, 2, 9));

        team.Members.Should().ContainSingle().Which.IsLead.Should().BeTrue();
        team.HasMember(AgentUser).Should().BeTrue();
        team.RemoveMember(AgentUser);
        team.HasMember(AgentUser).Should().BeFalse();
    }

    [Fact]
    public void Notifications_truncate_long_texts()
    {
        var notification = new Notification(AgentUser, null, NotificationType.SlaWarning, new string('t', 300), new string('m', 900), Local(2026, 10, 5, 10));

        notification.Title.Should().HaveLength(Notification.TitleMaxLength).And.EndWith("…");
        notification.Message.Should().HaveLength(Notification.MessageMaxLength);
        notification.MarkAsRead();
        notification.IsRead.Should().BeTrue();
    }

    [Fact]
    public void Category_update_changes_default_team()
    {
        var category = new Category(Guid.CreateVersion7(), "cards", "Tarjetas", "Tarjetas físicas", TeamA);

        category.Update("Tarjetas", "Tarjetas físicas y PIN", TeamB, isActive: true);

        category.DefaultTeamId.Should().Be(TeamB);
    }

    [Fact]
    public void Actor_reports_whether_it_is_staff()
    {
        new Actor(AgentUser, ActorRole.Agent).IsStaff.Should().BeTrue();
        new Actor(CustomerUser, ActorRole.Customer).IsStaff.Should().BeFalse();
        Actor.SystemActor.IsStaff.Should().BeFalse();
    }
}
