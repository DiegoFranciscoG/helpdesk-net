using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Reports;
using Helpdesk.Domain.Tickets;
using static Helpdesk.UnitTests.TestData;

namespace Helpdesk.UnitTests.Application;

public sealed class ReportCalculatorTests
{
    private static readonly DateTimeOffset From = Local(2026, 10, 1, 0, 0);
    private static readonly DateTimeOffset To = Local(2026, 10, 8, 0, 0);
    private static readonly Guid Agent1 = Guid.CreateVersion7();
    private static readonly Guid Agent2 = Guid.CreateVersion7();

    private static TicketReportRow Row(
        DateTimeOffset created,
        TicketStatus status,
        Priority priority = Priority.Medium,
        string? category = "Tarjetas",
        Guid? assignee = null,
        int? responseMinutes = 30,
        bool responseBreached = false,
        DateTimeOffset? resolved = null,
        bool resolutionBreached = false,
        int? resolutionMinutes = null,
        int? rating = null) =>
        new(Guid.CreateVersion7(), 1, created, status, priority, category, TeamA, "Cola", assignee, "CLI-00001",
            responseMinutes is null ? null : created.AddMinutes(responseMinutes.Value), responseBreached, responseMinutes,
            resolved, resolutionBreached, resolutionMinutes, rating);

    [Fact]
    public void Overview_computes_averages_compliance_and_csat()
    {
        IReadOnlyList<TicketReportRow> rows =
        [
            Row(Local(2026, 10, 1, 9, 0), TicketStatus.Closed, responseMinutes: 30, resolved: Local(2026, 10, 2, 9, 0), resolutionMinutes: 480, rating: 5),
            Row(Local(2026, 10, 2, 9, 0), TicketStatus.Closed, Priority.High, responseMinutes: 90, responseBreached: true, resolved: Local(2026, 10, 3, 9, 0), resolutionBreached: true, resolutionMinutes: 960, rating: 2),
            Row(Local(2026, 10, 6, 9, 0), TicketStatus.InProgress, Priority.High, category: null, responseMinutes: 60),
            Row(Local(2026, 10, 7, 9, 0), TicketStatus.New, Priority.Critical, responseMinutes: null, responseBreached: true),
            Row(Local(2026, 9, 1, 9, 0), TicketStatus.OnHold, responseMinutes: 20),
        ];

        var overview = ReportCalculator.Overview(rows, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), From, To, Guayaquil);

        overview.Created.Should().Be(4);
        overview.Resolved.Should().Be(2);
        overview.OpenNow.Should().Be(3);
        overview.BreachedOpenNow.Should().Be(1);
        overview.AverageFirstResponseBusinessHours.Should().Be(1.0, "(30 + 90 + 60) / 3 minutos = 60 min");
        overview.AverageResolutionBusinessHours.Should().Be(12.0);
        overview.FirstResponseCompliancePercent.Should().Be(50.0, "2 a tiempo de 4 medibles (incluye el vencido sin respuesta)");
        overview.ResolutionCompliancePercent.Should().Be(50.0);
        overview.CsatAverage.Should().Be(3.5);
        overview.CsatPercent.Should().Be(50.0);
        overview.SurveyCount.Should().Be(2);
        overview.ByPriority.Single(p => p.Priority == Priority.High).Created.Should().Be(2);
        overview.ByCategory.Should().Contain(c => c.Category == "Sin categoría" && c.Created == 1);
        overview.Daily.Should().HaveCount(7);
        overview.Daily.Single(d => d.Date == new DateOnly(2026, 10, 2)).Should().Match<Helpdesk.Contracts.Reports.DailyReportRow>(d => d.Created == 1 && d.Resolved == 1);
    }

    [Fact]
    public void Empty_period_returns_nulls_instead_of_division_by_zero()
    {
        var overview = ReportCalculator.Overview([], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), From, From.AddDays(1), Guayaquil);

        overview.Created.Should().Be(0);
        overview.AverageResolutionBusinessHours.Should().BeNull();
        overview.ResolutionCompliancePercent.Should().BeNull();
        overview.CsatAverage.Should().BeNull();
        overview.Daily.Should().ContainSingle();
    }

    [Fact]
    public void Agent_workload_counts_current_load_and_period_performance()
    {
        IReadOnlyList<TicketReportRow> rows =
        [
            Row(Local(2026, 10, 1, 9, 0), TicketStatus.Assigned, assignee: Agent1),
            Row(Local(2026, 10, 1, 9, 0), TicketStatus.InProgress, assignee: Agent1),
            Row(Local(2026, 10, 1, 9, 0), TicketStatus.OnHold, assignee: Agent1),
            Row(Local(2026, 10, 1, 9, 0), TicketStatus.Closed, assignee: Agent2, resolved: Local(2026, 10, 2, 9, 0), resolutionMinutes: 120, rating: 4),
        ];
        var agents = new Dictionary<Guid, string> { [Agent1] = "Ana", [Agent2] = "Beto" };

        var workload = ReportCalculator.AgentWorkload(rows, agents, From, To);

        workload[0].AgentName.Should().Be("Ana", "primero quien tiene más carga");
        workload[0].Should().Match<Helpdesk.Contracts.Reports.AgentWorkloadRow>(r => r.Assigned == 1 && r.InProgress == 1 && r.OnHold == 1 && r.ResolvedInPeriod == 0);
        workload[1].Should().Match<Helpdesk.Contracts.Reports.AgentWorkloadRow>(r => r.ResolvedInPeriod == 1 && r.AverageResolutionBusinessHours == 2.0 && r.CsatAverage == 4.0 && r.ResolutionCompliancePercent == 100.0);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("Pagos, transferencias", "\"Pagos, transferencias\"")]
    [InlineData("normal", "normal")]
    [InlineData("", "")]
    public void Csv_cells_neutralize_formulas_and_quote_separators(string input, string expected) =>
        ReportService.Escape(input).Should().Be(expected);
}
