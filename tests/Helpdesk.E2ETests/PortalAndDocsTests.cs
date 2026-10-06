using System.Text.Json;
using Helpdesk.E2ETests.Infrastructure;

namespace Helpdesk.E2ETests;

public sealed class PortalAndDocsTests(BrowserFixture fixture)
{
    [Fact]
    public async Task Customer_portal_fits_a_phone_screen()
    {
        fixture.SkipWhenDisabled();
        var page = await fixture.LoginAsync("luis.cardenas@correo.example", new() { Width = 390, Height = 844 });

        await page.GotoAsync("tickets");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Mis tickets" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Row).Nth(1)).ToBeVisibleAsync();

        // En pantallas angostas la tabla pasa a tarjetas: nada debe obligar a desplazarse en horizontal.
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        overflow.Should().BeLessThanOrEqualTo(0, "el portal del cliente debe usarse desde el teléfono");
        await fixture.ScreenshotAsync(page, "movil-cliente");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Api_documentation_is_public_and_explains_authentication()
    {
        fixture.SkipWhenDisabled();
        var page = await fixture.NewPageAsync();

        // La URL de la API se lee de la configuración pública del front, la misma que usa la app.
        var response = await page.APIRequest.GetAsync("appsettings.json");
        using var settings = JsonDocument.Parse(await response.TextAsync());
        var apiUrl = settings.RootElement.GetProperty("Api").GetProperty("BaseUrl").GetString()!.TrimEnd('/');

        await page.GotoAsync($"{apiUrl}/docs");
        await Expect(page.GetByText("helpdesk-net API").First).ToBeVisibleAsync();
        await Expect(page.GetByText("Autenticación JWT Bearer", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(page.GetByText("/api/v1/auth/login").First).ToBeVisibleAsync();
        await fixture.ScreenshotAsync(page, "api-docs");
        await page.Context.CloseAsync();
    }
}
