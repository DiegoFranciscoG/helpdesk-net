namespace Helpdesk.E2ETests.Infrastructure;

/// <summary>
/// Las pruebas de extremo a extremo corren contra un stack ya levantado (docker compose o local).
/// Sin <c>HELPDESK_E2E_WEB_URL</c> se omiten, así <c>dotnet test</c> sigue funcionando sin navegador.
/// </summary>
public sealed record E2ESettings(string? WebUrl, string? Password, string? ScreenshotsPath, bool Headed)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(WebUrl) && !string.IsNullOrWhiteSpace(Password);

    public static E2ESettings FromEnvironment() => new(
        Environment.GetEnvironmentVariable("HELPDESK_E2E_WEB_URL"),
        Environment.GetEnvironmentVariable("HELPDESK_E2E_PASSWORD"),
        Environment.GetEnvironmentVariable("HELPDESK_E2E_SCREENSHOTS"),
        Environment.GetEnvironmentVariable("HELPDESK_E2E_HEADED") == "1");
}
