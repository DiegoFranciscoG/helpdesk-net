using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Helpdesk.Contracts.Auth;
using Helpdesk.Contracts.Catalog;
using Helpdesk.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

public sealed class AuthAndSecurityTests(HelpdeskFixture fixture)
{
    [Fact]
    public async Task Login_errors_are_generic_and_do_not_reveal_whether_the_user_exists()
    {
        var client = fixture.Factory.CreateClient();

        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = "nadie@helpdesk.example", Password = "una-clave-cualquiera-larga" });
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = "supervisor@helpdesk.example", Password = "una-clave-cualquiera-larga" });

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("Credenciales inválidas");
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Credenciales inválidas");
    }

    [Fact]
    public async Task Account_is_locked_after_five_failed_attempts()
    {
        var (email, password) = await CreateAgentAsync();
        var client = fixture.Factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password + "x" });
        }

        var withRightPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        withRightPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "la cuenta queda bloqueada 15 minutos");
    }

    [Fact]
    public async Task Refresh_tokens_rotate_and_reuse_revokes_the_whole_family()
    {
        var (email, password) = await CreateAgentAsync();
        var client = fixture.Factory.CreateClient();
        var login = await HelpdeskFixture.LoginAsync(client, email, password);

        var rotated = await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = login.RefreshToken })).ReadAsync<TokenResponse>();
        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = login.RefreshToken });
        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = rotated.RefreshToken });

        rotated.RefreshToken.Should().NotBe(login.RefreshToken);
        rotated.RefreshTokenExpiresAt.Should().Be(login.RefreshTokenExpiresAt, "la expiración es absoluta para toda la familia");
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        afterReuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "la reutilización revoca toda la familia");
    }

    [Fact]
    public async Task Logout_revokes_the_session()
    {
        var (email, password) = await CreateAgentAsync();
        var client = fixture.Factory.CreateClient();
        var login = await HelpdeskFixture.LoginAsync(client, email, password);

        (await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest { RefreshToken = login.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = login.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Passwords_are_hashed_with_the_configured_iteration_count()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<global::Helpdesk.Infrastructure.Persistence.HelpdeskDbContext>();
        var hash = await db.Users.Where(u => u.Email == "supervisor@helpdesk.example").Select(u => u.PasswordHash).SingleAsync();

        var bytes = Convert.FromBase64String(hash!);
        bytes[0].Should().Be(0x01, "formato Identity V3");
        var iterations = (bytes[5] << 24) | (bytes[6] << 16) | (bytes[7] << 8) | bytes[8];
        iterations.Should().Be(220_000);
    }

    [Theory]
    [InlineData("corta-123")]
    [InlineData("contraseña123456")]
    [InlineData("aaaaaaaaaaaaaaaaaaa")]
    [InlineData("1234567890123456789")]
    public async Task Weak_passwords_are_rejected(string password)
    {
        var admin = await fixture.ClientAsAsync("admin");

        var response = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest
        {
            FullName = "Prueba Débil",
            Email = $"debil-{Guid.NewGuid():N}@helpdesk.example",
            Password = password,
            Role = "Agent",
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Endpoints_deny_by_default_and_integration_tokens_only_reach_integration_routes()
    {
        var anonymous = fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/tickets")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/categories")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var token = await (await anonymous.PostAsJsonAsync("/api/v1/auth/token", new ClientTokenRequest { ClientId = HelpdeskFixture.IntegrationClientId, ClientSecret = fixture.IntegrationSecret }))
            .ReadAsync<ClientTokenResponse>();
        var integration = fixture.Factory.CreateClient();
        integration.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        token.Scopes.Should().BeEquivalentTo(["tickets:read", "suggestions:write"]);
        (await integration.GetAsync("/api/v1/tickets")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await integration.GetAsync("/api/v1/integration/tickets/unclassified")).StatusCode.Should().Be(HttpStatusCode.OK);

        var badSecret = await anonymous.PostAsJsonAsync("/api/v1/auth/token", new ClientTokenRequest { ClientId = HelpdeskFixture.IntegrationClientId, ClientSecret = "incorrecto" });
        badSecret.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Users_cannot_reach_integration_routes_and_roles_are_enforced()
    {
        var agent = await fixture.ClientAsAsync("agente");
        var customer = await fixture.ClientAsAsync("cliente");

        (await agent.GetAsync("/api/v1/integration/tickets/unclassified")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.GetAsync("/api/v1/reports/overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/api/v1/teams")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/api/v1/customers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/api/v1/categories")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var client = await fixture.ClientAsAsync("agente");

        var response = await client.GetAsync("/api/v1/categories");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.Contains("Server").Should().BeFalse();
    }

    [Fact]
    public async Task Cors_allows_only_the_configured_front_origin()
    {
        var client = fixture.Factory.CreateClient();

        var allowed = await Preflight(client, "https://front.example");
        var denied = await Preflight(client, "https://evil.example");

        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://front.example");
        denied.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Health_and_openapi_are_public()
    {
        var client = fixture.Factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        var openApi = await client.GetStringAsync("/openapi/v1.json");
        openApi.Should().Contain("\"Bearer\"").And.Contain("/api/v1/tickets/{id}/transitions");
    }

    [Fact]
    public async Task Rate_limit_returns_429_with_retry_after()
    {
        await using var factory = fixture.CreateFactory("helpdesk_ratelimit", new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPerMinute"] = "3",
            ["Database:SeedDemoData"] = "false",
        });
        var client = factory.CreateClient();

        HttpResponseMessage last = null!;
        for (var i = 0; i < 4; i++)
        {
            last = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = "nadie@helpdesk.example", Password = "una-clave-cualquiera-larga" });
        }

        last.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        last.Headers.RetryAfter.Should().NotBeNull();
        (await last.ProblemCodeAsync()).Should().Be("rate_limited");
    }

    private static Task<HttpResponseMessage> Preflight(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/tickets");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return client.SendAsync(request);
    }

    private async Task<(string Email, string Password)> CreateAgentAsync()
    {
        var admin = await fixture.ClientAsAsync("admin");
        var email = $"agente-{Guid.NewGuid():N}@helpdesk.example";
        var password = "Clave-Segura-" + Guid.NewGuid().ToString("N")[..12];
        var response = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest { FullName = "Agente Temporal", Email = email, Password = password, Role = "Agent" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (email, password);
    }
}
