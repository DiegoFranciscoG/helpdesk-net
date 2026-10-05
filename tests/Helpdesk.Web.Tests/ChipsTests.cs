using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Tickets;
using Helpdesk.Web.Components;

namespace Helpdesk.Web.Tests;

public sealed class ChipsTests : MudTestContext
{
    [Fact]
    public void Status_chip_color_follows_live_status_changes()
    {
        var cut = Render<Chips>(p => p.Add(c => c.Kind, Chips.ChipKind.Status).Add(c => c.Status, TicketStatus.InProgress));
        cut.Find(".mud-chip").ClassList.Should().Contain("mud-chip-color-secondary");

        // Por SignalR el mismo componente recibe el estado nuevo: el color no puede quedarse en el anterior.
        cut.Render(p => p.Add(c => c.Status, TicketStatus.Resolved));

        var chip = cut.Find(".mud-chip");
        chip.TextContent.Should().Contain("Resuelto");
        chip.ClassList.Should().Contain("mud-chip-color-success").And.NotContain("mud-chip-color-secondary");
    }

    [Theory]
    [InlineData(Priority.Critical, "P1 Crítica", "mud-chip-color-error")]
    [InlineData(Priority.High, "P2 Alta", "mud-chip-color-warning")]
    [InlineData(Priority.Planning, "P5 Planificación", "mud-chip-color-default")]
    public void Priority_chip_shows_text_and_color(Priority priority, string text, string colorClass)
    {
        var cut = Render<Chips>(p => p.Add(c => c.Kind, Chips.ChipKind.Priority).Add(c => c.Priority, priority));

        cut.Find(".mud-chip").TextContent.Should().Contain(text);
        cut.Find(".mud-chip").ClassList.Should().Contain(colorClass);
    }

    [Theory]
    [InlineData(SlaState.Breached, "Incumplido")]
    [InlineData(SlaState.AtRisk, "En riesgo")]
    [InlineData(SlaState.Paused, "Pausado")]
    public void Sla_chip_never_relies_on_color_alone(SlaState state, string text)
    {
        var cut = Render<Chips>(p => p.Add(c => c.Kind, Chips.ChipKind.Sla).Add(c => c.Sla, state));

        cut.Find(".mud-chip").TextContent.Should().Contain(text, "WCAG 1.4.1: el color siempre va con texto");
        cut.FindAll(".mud-chip svg").Should().NotBeEmpty("y con un ícono");
    }
}
