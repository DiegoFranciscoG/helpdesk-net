using Helpdesk.Web.Services;

namespace Helpdesk.Web.Tests;

public sealed class LabelsAndReturnUrlTests
{
    [Theory]
    [InlineData(30, true, "30 min")]
    [InlineData(240, true, "4 h")]
    [InlineData(480, false, "8 h hábiles (1 día)")]
    [InlineData(1440, false, "24 h hábiles (3 días)")]
    [InlineData(90, false, "1,5 h hábiles")]
    public void Sla_targets_are_shown_in_business_hours_with_workday_equivalent(int minutes, bool is24x7, string expected) =>
        Labels.Target(minutes, is24x7).Should().Be(expected);

    [Fact]
    public void Relative_due_text_reads_naturally()
    {
        var now = new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

        Labels.Relative(now.AddMinutes(130), now).Should().Be("vence en 2 h 10 min");
        Labels.Relative(now.AddMinutes(-35), now).Should().Be("venció hace 35 min");
        Labels.Relative(now.AddDays(2).AddHours(3), now).Should().Be("vence en 2 d 3 h");
    }

    [Theory]
    [InlineData("tickets/nuevo", "tickets/nuevo")]
    [InlineData("/reportes", "reportes")]
    [InlineData(null, "")]
    [InlineData("//evil.example", "")]
    [InlineData("https://evil.example", "")]
    [InlineData("/\\evil.example", "")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("tickets\n//evil.example", "")]
    public void Return_url_only_allows_internal_relative_routes(string? input, string expected) =>
        ReturnUrls.Sanitize(input).Should().Be(expected);
}
