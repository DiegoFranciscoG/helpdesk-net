using Helpdesk.E2ETests.Infrastructure;

namespace Helpdesk.E2ETests;

public sealed class DashboardTests(BrowserFixture fixture)
{
    [Fact]
    public async Task Supervisor_sees_sla_risk_reports_and_business_calendar()
    {
        fixture.SkipWhenDisabled();
        var page = await fixture.LoginAsync("supervisor");

        await Expect(page.GetByText("Hola, Lucía")).ToBeVisibleAsync();
        // La bandeja demo trae tickets incumplidos y en riesgo: deben aparecer como filas, no solo en los títulos.
        await Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Incumplido" }).First).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "En riesgo" }).First).ToBeVisibleAsync();
        await fixture.ScreenshotAsync(page, "tablero");

        await page.GotoAsync("reportes");
        await Expect(page.GetByText("Cumplimiento de SLA por prioridad")).ToBeVisibleAsync();
        await Expect(page.GetByText("Carga por agente")).ToBeVisibleAsync();
        await fixture.ScreenshotAsync(page, "reportes");

        await page.GotoAsync("admin/sla");
        await Expect(page.GetByText("Independencia de Guayaquil")).ToBeVisibleAsync();
        await Expect(page.GetByText("24 h hábiles (3 días)").First).ToBeVisibleAsync();
        await fixture.ScreenshotAsync(page, "sla-calendario");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Customers_cannot_open_staff_pages_and_only_see_their_own_tickets()
    {
        fixture.SkipWhenDisabled();
        var page = await fixture.LoginAsync("luis.cardenas@correo.example");

        await page.GotoAsync("reportes");
        await Expect(page.GetByText("No tienes permiso para ver esta página.")).ToBeVisibleAsync();
        await Expect(page.GetByText("Cumplimiento de SLA por prioridad")).Not.ToBeVisibleAsync();

        await page.GotoAsync("tickets");
        await Expect(page.GetByRole(AriaRole.Row).Nth(1)).ToBeVisibleAsync();
        await Expect(page.GetByText("Valeria Guzmán")).Not.ToBeVisibleAsync();
        await Expect(page.GetByText("Textiles Nubeverde")).Not.ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }
}
