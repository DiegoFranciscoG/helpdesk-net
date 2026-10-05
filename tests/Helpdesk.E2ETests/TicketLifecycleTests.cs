using System.Globalization;
using System.Text.RegularExpressions;
using Helpdesk.E2ETests.Infrastructure;

namespace Helpdesk.E2ETests;

/// <summary>
/// Flujo completo con tres personas en navegadores separados: el cliente registra el ticket, la supervisora
/// lo ve aparecer sin recargar y lo asigna, el agente lo atiende y el cliente ve la solución en vivo (SignalR).
/// </summary>
public sealed partial class TicketLifecycleTests(BrowserFixture fixture)
{
    [Fact]
    public async Task Customer_ticket_flows_live_from_portal_to_resolution()
    {
        fixture.SkipWhenDisabled();
        var stamp = DateTime.UtcNow.ToString("HHmmssfff", CultureInfo.InvariantCulture);
        var title = $"Transferencia retenida {stamp}";
        var reply = $"Estamos revisando la transferencia {stamp} con el banco destino.";
        var resolution = $"Se liberó la transferencia {stamp} y se confirmó la acreditación.";

        var supervisor = await fixture.LoginAsync("supervisor");
        await supervisor.GotoAsync("tickets");
        await Expect(supervisor.GetByRole(AriaRole.Row).Nth(1)).ToBeVisibleAsync();

        var customer = await fixture.LoginAsync("luis.cardenas@correo.example");
        await customer.GotoAsync("tickets/nuevo");
        await customer.GetByLabel("Título").FillAsync(title);
        await customer.GetByLabel("Describe el problema").FillAsync("Hice una transferencia a un proveedor esta mañana y sigue en estado pendiente.");
        await customer.GetByRole(AriaRole.Button, new() { Name = "Registrar ticket" }).ClickAsync();
        await customer.WaitForURLAsync(TicketUrl());
        await Expect(customer.GetByText("Nuevo", new() { Exact = true }).First).ToBeVisibleAsync();

        // Sin recargar: la lista de la supervisora se actualiza por SignalR.
        var row = supervisor.GetByRole(AriaRole.Row).Filter(new() { HasText = title });
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.GetByRole(AriaRole.Link).First.ClickAsync();
        await supervisor.WaitForURLAsync(TicketUrl());
        await supervisor.GetByRole(AriaRole.Button, new() { Name = "Asignar", Exact = true }).ClickAsync();
        var dialog = supervisor.GetByRole(AriaRole.Dialog);
        await dialog.GetByRole(AriaRole.Combobox, new() { Name = "Asignar a" }).ClickAsync();
        await supervisor.GetByRole(AriaRole.Option).Filter(new() { HasText = "Carlos Vélez" }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Asignar", Exact = true }).ClickAsync();
        await Expect(supervisor.GetByText("Asignado", new() { Exact = true }).First).ToBeVisibleAsync();
        var ticketUrl = supervisor.Url;

        var agent = await fixture.LoginAsync("agente");
        await agent.GotoAsync(ticketUrl);
        await agent.GetByRole(AriaRole.Button, new() { Name = "Iniciar" }).ClickAsync();
        await Expect(agent.GetByText("En proceso", new() { Exact = true }).First).ToBeVisibleAsync();
        await agent.GetByLabel("Escribe una respuesta").FillAsync(reply);
        await agent.GetByRole(AriaRole.Button, new() { Name = "Enviar" }).ClickAsync();
        await Expect(agent.GetByText(reply)).ToBeVisibleAsync();

        // El cliente recibe la respuesta pública en vivo, sin recargar.
        await Expect(customer.GetByText(reply)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await agent.GetByRole(AriaRole.Button, new() { Name = "Resolver" }).ClickAsync();
        var resolveDialog = agent.GetByRole(AriaRole.Dialog);
        await resolveDialog.GetByLabel("Solución aplicada").FillAsync(resolution);
        await resolveDialog.GetByRole(AriaRole.Button, new() { Name = "Confirmar" }).ClickAsync();
        await Expect(agent.GetByText("Resuelto", new() { Exact = true }).First).ToBeVisibleAsync();
        // El chip cambió de estado en vivo varias veces: su clase de color debe ser la del estado actual.
        await Expect(agent.Locator(".mud-chip").Filter(new() { HasText = "Resuelto" }).First).ToHaveClassAsync(new Regex("mud-chip-color-success"));
        await fixture.ScreenshotAsync(agent, "ticket-detalle-agente");

        await Expect(customer.GetByText(resolution)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(customer.GetByText("Resuelto", new() { Exact = true }).First).ToBeVisibleAsync();
        await fixture.ScreenshotAsync(customer, "ticket-detalle-cliente");

        await supervisor.Context.CloseAsync();
        await customer.Context.CloseAsync();
        await agent.Context.CloseAsync();
    }

    [Fact]
    public async Task Priority_preview_follows_the_itil_matrix_before_submitting()
    {
        fixture.SkipWhenDisabled();
        var customer = await fixture.LoginAsync("luis.cardenas@correo.example");
        await customer.GotoAsync("tickets/nuevo");

        await customer.GetByRole(AriaRole.Combobox, new() { Name = "Impacto" }).ClickAsync();
        await customer.GetByRole(AriaRole.Option).Filter(new() { HasText = "Alto:" }).ClickAsync();
        await customer.GetByRole(AriaRole.Combobox, new() { Name = "Urgencia" }).ClickAsync();
        await customer.GetByRole(AriaRole.Option).Filter(new() { HasText = "Alta:" }).ClickAsync();

        await Expect(customer.GetByText("P1 Crítica")).ToBeVisibleAsync();
        await Expect(customer.GetByText("resolución en 4 h (24×7)", new() { Exact = false })).ToBeVisibleAsync();
        await fixture.ScreenshotAsync(customer, "nuevo-ticket");
        await customer.Context.CloseAsync();
    }

    [GeneratedRegex(@"/tickets/[0-9a-f-]{36}$")]
    private static partial Regex TicketUrl();
}
