[assembly: AssemblyFixture(typeof(Helpdesk.E2ETests.Infrastructure.BrowserFixture))]

namespace Helpdesk.E2ETests.Infrastructure;

/// <summary>Un solo Chromium por corrida; cada actor (cliente, supervisora, agente) usa su propio contexto aislado.</summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    public const string DemoDomain = "helpdesk.example";
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public E2ESettings Settings { get; } = E2ESettings.FromEnvironment();

    public async ValueTask InitializeAsync()
    {
        if (!Settings.Enabled)
        {
            return;
        }

        // La primera carga de Blazor WebAssembly descarga el runtime: 5 s por defecto es poco en CI.
        SetDefaultExpectTimeout(15_000);
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = !Settings.Headed });
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    public void SkipWhenDisabled() =>
        Assert.SkipUnless(Settings.Enabled, "Define HELPDESK_E2E_WEB_URL y HELPDESK_E2E_PASSWORD para correr las pruebas e2e.");

    /// <summary>Contexto nuevo (cookies y sessionStorage propios) con la sesión iniciada.</summary>
    public async Task<IPage> LoginAsync(string email)
    {
        var context = await _browser!.NewContextAsync(new()
        {
            BaseURL = Settings.WebUrl!.TrimEnd('/') + "/",
            Locale = "es-EC",
            TimezoneId = "America/Guayaquil",
            ViewportSize = new() { Width = 1440, Height = 900 },
        });
        context.SetDefaultTimeout(30_000);
        var page = await context.NewPageAsync();

        await page.GotoAsync("login");
        await page.GetByLabel("Correo", new() { Exact = true }).FillAsync(email.Contains('@', StringComparison.Ordinal) ? email : $"{email}@{DemoDomain}");
        await page.GetByLabel("Contraseña", new() { Exact = true }).FillAsync(Settings.Password!);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        try
        {
            await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.Ordinal));
        }
        catch (TimeoutException ex)
        {
            if (await page.GetByText("Demasiados intentos").IsVisibleAsync())
            {
                throw new InvalidOperationException("La API limitó los inicios de sesión (429). Para e2e levanta la API con RateLimiting__AuthPerMinute=60.", ex);
            }

            await ScreenshotAsync(page, $"fallo-login-{email.Split('@')[0]}");
            var alerts = await page.GetByRole(AriaRole.Alert).AllInnerTextsAsync();
            throw new InvalidOperationException($"El login de {email} no salió de /login ({page.Url}). Alertas: {string.Join(" | ", alerts)}", ex);
        }

        return page;
    }

    /// <summary>Guarda una captura para el README cuando se define HELPDESK_E2E_SCREENSHOTS.</summary>
    public async Task ScreenshotAsync(IPage page, string name)
    {
        if (string.IsNullOrWhiteSpace(Settings.ScreenshotsPath))
        {
            return;
        }

        Directory.CreateDirectory(Settings.ScreenshotsPath);
        // Sin animaciones: las transiciones de color de MudBlazor saldrían a medio camino en la captura.
        await page.ScreenshotAsync(new() { Path = Path.Combine(Settings.ScreenshotsPath, $"{name}.png"), Animations = ScreenshotAnimations.Disabled });
    }
}
