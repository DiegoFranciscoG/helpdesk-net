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

    [Fact]
    public async Task Agent_who_transfers_outside_their_queues_is_sent_back_to_the_list()
    {
        fixture.SkipWhenDisabled();
        var ticketUrl = await CreateTicketAsCustomerAsync($"Revisión de acceso {Stamp()}", "No reconozco un inicio de sesión en mi banca en línea desde otra ciudad.");

        // Carlos atiende la Mesa de Servicio; al pasar el ticket a Fraude deja de tener acceso (404 por diseño).
        var agent = await fixture.LoginAsync("agente");
        await agent.GotoAsync(ticketUrl);
        await agent.GetByRole(AriaRole.Button, new() { Name = "Transferir" }).ClickAsync();
        var dialog = agent.GetByRole(AriaRole.Dialog);
        await dialog.GetByRole(AriaRole.Combobox, new() { Name = "Cola de destino" }).ClickAsync();
        await agent.GetByRole(AriaRole.Option).Filter(new() { HasText = "Fraude y Seguridad" }).ClickAsync();
        await dialog.GetByLabel("Motivo").FillAsync("Posible acceso no autorizado: lo revisa el equipo de fraude.");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Transferir" }).ClickAsync();

        await Expect(agent.GetByText("pasó a otra cola y ya no está entre las tuyas")).ToBeVisibleAsync();
        await Expect(agent).ToHaveURLAsync(new Regex("/tickets$"));
        await agent.Context.CloseAsync();
    }

    [Fact]
    public async Task Supervisor_reviews_the_real_classifier_suggestion()
    {
        fixture.SkipWhenDisabled();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HELPDESK_E2E_NLP") == "1", "Requiere nlp-tickets-es (P27) conectado: define HELPDESK_E2E_NLP=1.");
        var ticketUrl = await CreateTicketAsCustomerAsync($"Compras desconocidas {Stamp()}", "Aparecen tres compras en línea que no hice con mi tarjeta de crédito. Necesito bloquearla de inmediato.");

        var supervisor = await fixture.LoginAsync("supervisor");
        await supervisor.GotoAsync(ticketUrl);
        await supervisor.GetByRole(AriaRole.Tab, new() { Name = "Sugerencia IA" }).ClickAsync();
        // El worker clasifica el ticket recién creado y la notificación recarga la vista en vivo.
        await Expect(supervisor.GetByText("Pendiente de revisión")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(supervisor.GetByText("nlp-tickets-es · modelo", new() { Exact = false })).ToBeVisibleAsync();
        await fixture.ScreenshotAsync(supervisor, "sugerencia-ia");

        await supervisor.GetByRole(AriaRole.Button, new() { Name = "Aceptar" }).ClickAsync();
        await Expect(supervisor.GetByText("Aceptada", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(supervisor.GetByText("Revisada por Lucía Paredes", new() { Exact = false })).ToBeVisibleAsync();
        await supervisor.Context.CloseAsync();
    }

    private async Task<string> CreateTicketAsCustomerAsync(string title, string description)
    {
        var customer = await fixture.LoginAsync("luis.cardenas@correo.example");
        await customer.GotoAsync("tickets/nuevo");
        await customer.GetByLabel("Título").FillAsync(title);
        await customer.GetByLabel("Describe el problema").FillAsync(description);
        await customer.GetByRole(AriaRole.Button, new() { Name = "Registrar ticket" }).ClickAsync();
        await customer.WaitForURLAsync(TicketUrl());
        var url = customer.Url;
        await customer.Context.CloseAsync();
        return url;
    }

    private static string Stamp() => DateTime.UtcNow.ToString("HHmmssfff", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"/tickets/[0-9a-f-]{36}$")]
    private static partial Regex TicketUrl();
}
