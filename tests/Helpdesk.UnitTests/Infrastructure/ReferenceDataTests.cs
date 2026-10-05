using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Persistence.Converters;
using Helpdesk.Infrastructure.Persistence.Seed;

namespace Helpdesk.UnitTests.Infrastructure;

public sealed class ReferenceDataTests
{
    [Fact]
    public void Every_category_routes_to_an_existing_team()
    {
        var teamIds = ReferenceData.Teams.Select(t => t.Id).ToHashSet();

        ReferenceData.Categories.Should().OnlyContain(c => teamIds.Contains(c.DefaultTeamId));
    }

    [Fact]
    public void Team_and_category_codes_match_the_nlp_catalog()
    {
        ReferenceData.Teams.Select(t => t.Code).Should().Contain(
            ["card_operations", "digital_support", "payments_transfers", "fraud_security", "kyc_compliance", "customer_care"]);
        ReferenceData.Categories.Select(c => c.Code).Should().Contain(
            ["cards", "digital_channels", "card_payments", "cash_atm", "transfers", "top_ups", "fees_fx", "security_fraud", "account_kyc"]);
    }

    [Fact]
    public void Codes_and_ids_are_unique()
    {
        ReferenceData.Teams.Select(t => t.Code).Should().OnlyHaveUniqueItems();
        ReferenceData.Categories.Select(c => c.Code).Should().OnlyHaveUniqueItems();
        ReferenceData.SlaPolicies.Select(p => p.Id).Should().OnlyHaveUniqueItems();
        ReferenceData.Holidays2026.Select(h => h.Date).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_priority_has_a_general_sla_policy()
    {
        foreach (var priority in Enum.GetValues<Priority>())
        {
            ReferenceData.SlaPolicies.Should().ContainSingle(p => p.Priority == priority && p.Tier == null);
        }
    }

    [Fact]
    public void Sla_targets_are_valid_domain_policies()
    {
        foreach (var p in ReferenceData.SlaPolicies)
        {
            var act = () => new SlaPolicy(p.Id, p.Name, p.Priority, p.Tier, p.Response, p.Resolution, 75, p.CalendarId);
            act.Should().NotThrow($"la regla {p.Name} debe ser válida");
        }
    }

    [Fact]
    public void Critical_priority_uses_the_24x7_calendar()
    {
        ReferenceData.SlaPolicies.Where(p => p.Priority == Priority.Critical)
            .Should().OnlyContain(p => p.CalendarId == ReferenceData.TwentyFourSevenCalendarId);
    }

    [Fact]
    public void Holidays_2026_follow_the_official_calendar()
    {
        ReferenceData.Holidays2026.Should().HaveCount(12);
        ReferenceData.Holidays2026.Should().Contain(h => h.Date == new DateOnly(2026, 5, 25) && h.LegalBasis.Contains("R.O. 906", StringComparison.Ordinal));
        ReferenceData.Holidays2026.Should().Contain(h => h.Date == new DateOnly(2026, 1, 2) && h.LegalBasis.Contains("249", StringComparison.Ordinal));
        ReferenceData.Holidays2026.Should().OnlyContain(h => h.Date.DayOfWeek != DayOfWeek.Saturday && h.Date.DayOfWeek != DayOfWeek.Sunday);
    }

    [Fact]
    public void Snake_case_converter_round_trips_every_status()
    {
        var converter = new SnakeCaseEnumConverter<TicketStatus>();
        var toDb = converter.ConvertToProviderExpression.Compile();
        var fromDb = converter.ConvertFromProviderExpression.Compile();

        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            fromDb(toDb(status)).Should().Be(status);
        }

        SnakeCaseEnumConverter<TicketStatus>.ToDatabase(TicketStatus.InProgress).Should().Be("in_progress");
        SnakeCaseEnumConverter<TicketChannel>.SqlList.Should().Contain("'walk_in'");
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, 1)]
    [InlineData(DayOfWeek.Saturday, 6)]
    [InlineData(DayOfWeek.Sunday, 7)]
    public void Day_of_week_is_stored_in_iso_numbering(DayOfWeek day, short iso)
    {
        var converter = new IsoDayOfWeekConverter();

        converter.ConvertToProviderExpression.Compile()(day).Should().Be(iso);
        converter.ConvertFromProviderExpression.Compile()(iso).Should().Be(day);
    }

    [Fact]
    public void Timestamps_are_normalized_to_utc()
    {
        var converter = new UtcDateTimeOffsetConverter();
        var local = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(-5));

        converter.ConvertToProviderExpression.Compile()(local).Offset.Should().Be(TimeSpan.Zero);
    }
}
