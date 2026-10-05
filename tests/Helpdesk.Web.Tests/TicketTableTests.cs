using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Tickets;
using Helpdesk.Web.Components;

namespace Helpdesk.Web.Tests;

public sealed class TicketTableTests : MudTestContext
{
    [Fact]
    public void Full_table_shows_queue_and_creation_columns()
    {
        var cut = Render<TicketTable>(p => p.Add(t => t.Items, [Item(firstRespondedAt: DateTimeOffset.UtcNow)]));

        Headers(cut).Should().Equal("Número", "Título", "Estado", "Prioridad", "SLA", "Cliente", "Cola / asignado", "Creado");
        cut.Markup.Should().Contain("Mesa de Servicio N1").And.Contain("Sin asignar");
    }

    [Fact]
    public void Compact_table_keeps_only_what_fits_in_a_dashboard_card()
    {
        var cut = Render<TicketTable>(p => p.Add(t => t.Items, [Item(firstRespondedAt: DateTimeOffset.UtcNow)]).Add(t => t.Compact, true).Add(t => t.ShowCustomer, false));

        Headers(cut).Should().Equal("Número", "Título", "Estado", "SLA");
        cut.FindAll(".chip-stack .mud-chip").Should().HaveCount(2, "estado y prioridad comparten celda");
    }

    [Fact]
    public void Pending_first_response_drives_the_relative_due_text()
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = Item(firstRespondedAt: null, firstResponseDue: now.AddMinutes(-95), resolutionDue: now.AddHours(5), sla: SlaState.Breached);

        var cut = Render<TicketTable>(p => p.Add(t => t.Items, [ticket]));

        // Antes se mostraba "vence en 5 h" junto a "Incumplido": contradictorio. Manda el reloj que incumplió.
        cut.Markup.Should().Contain("1.ª respuesta: venció hace 1 h 35 min").And.NotContain("vence en 4 h");
    }

    [Fact]
    public void After_first_response_the_resolution_clock_is_shown()
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = Item(firstRespondedAt: now.AddHours(-1), firstResponseDue: now.AddHours(-2), resolutionDue: now.AddMinutes(150), sla: SlaState.AtRisk);

        var cut = Render<TicketTable>(p => p.Add(t => t.Items, [ticket]));

        cut.Markup.Should().Contain("Resolución: vence en 2 h 2");
    }

    [Fact]
    public void Empty_list_shows_the_given_message()
    {
        var cut = Render<TicketTable>(p => p.Add(t => t.Items, []).Add(t => t.EmptyText, "Ningún ticket en riesgo."));

        cut.Markup.Should().Contain("Ningún ticket en riesgo.");
    }

    private static string[] Headers(IRenderedComponent<TicketTable> cut) =>
        [.. cut.FindAll("th").Select(th => th.TextContent.Trim())];

    private static TicketListItemDto Item(
        DateTimeOffset? firstRespondedAt,
        DateTimeOffset? firstResponseDue = null,
        DateTimeOffset? resolutionDue = null,
        SlaState sla = SlaState.OnTrack)
    {
        var now = DateTimeOffset.UtcNow;
        return new TicketListItemDto(
            Guid.CreateVersion7(), "HD-000001", "Transferencia no llega", TicketStatus.InProgress, Priority.Medium, Impact.Medium, Urgency.Medium,
            null, Guid.CreateVersion7(), "Mesa de Servicio N1", null, null, "CLI-00003", "Luis Cárdenas", now.AddHours(-3),
            firstResponseDue ?? now.AddHours(1), firstRespondedAt, resolutionDue ?? now.AddHours(8), sla, 0);
    }
}
