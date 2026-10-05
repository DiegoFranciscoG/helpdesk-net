using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Helpdesk.Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using WireMock.Server;

[assembly: AssemblyFixture(typeof(Helpdesk.IntegrationTests.Infrastructure.HelpdeskFixture))]

namespace Helpdesk.IntegrationTests.Infrastructure;

/// <summary>
/// Una base PostgreSQL 18 real (Testcontainers), un P27 simulado (WireMock) y la API completa en memoria
/// con migraciones y datos demo. Los secretos se generan al vuelo: no hay ninguno en el repositorio.
/// </summary>
public sealed class HelpdeskFixture : IAsyncLifetime
{
    public const string DemoDomain = "helpdesk.example";
    public const string IntegrationClientId = "nlp-tickets-es";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine3.24").Build();
    private readonly Dictionary<string, TokenResponse> _tokens = [];
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public static JsonSerializerOptions Json { get; } = CreateJson();

    public string DemoPassword { get; } = "Integracion-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    public string IntegrationSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string NlpClientKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public string NlpReviewerKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "helpdesk-it-" + Guid.NewGuid().ToString("N"));

    public WireMockServer Nlp { get; private set; } = null!;

    public HelpdeskApiFactory Factory { get; private set; } = null!;

    public string ConnectionString(string database = "helpdesk") =>
        new Npgsql.NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Nlp = WireMockServer.Start();
        Factory = CreateFactory("helpdesk");
        _ = Factory.Server;
    }

    public HelpdeskApiFactory CreateFactory(string database, IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Helpdesk"] = ConnectionString(database),
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Database:ApplyMigrationsOnStartup"] = "true",
            ["Database:SeedDemoData"] = "true",
            ["Seed:DemoPassword"] = DemoPassword,
            ["Workers:Enabled"] = "false",
            ["Nlp:BaseUrl"] = Nlp.Url,
            ["Nlp:ApiKey"] = NlpClientKey,
            ["Nlp:ReviewerApiKey"] = NlpReviewerKey,
            ["Nlp:TimeoutSeconds"] = "5",
            ["Integration:ClientId"] = IntegrationClientId,
            ["Integration:ClientSecret"] = IntegrationSecret,
            ["RateLimiting:AuthPerMinute"] = "10000",
            ["RateLimiting:RefreshPerMinute"] = "10000",
            ["RateLimiting:UploadsPerMinute"] = "10000",
            ["RateLimiting:GlobalPerMinute"] = "100000",
            ["Storage:AttachmentsPath"] = Path.Combine(StoragePath, database),
            ["Cors:AllowedOrigins"] = "https://front.example",
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Error",
            ["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Error",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return new HelpdeskApiFactory(settings);
    }

    /// <summary>Cliente HTTP autenticado como un usuario demo (el token se reutiliza durante la corrida).</summary>
    public async Task<HttpClient> ClientAsAsync(string emailLocalPart)
    {
        var email = emailLocalPart.Contains('@', StringComparison.Ordinal) ? emailLocalPart : $"{emailLocalPart}@{DemoDomain}";
        var token = await TokenAsync(email);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    public async Task<TokenResponse> TokenAsync(string email)
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_tokens.TryGetValue(email, out var cached) && cached.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cached;
            }

            var token = await LoginAsync(Factory.CreateClient(), email, DemoPassword);
            _tokens[email] = token;
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public static async Task<TokenResponse> LoginAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
    }

    /// <summary>El cliente demo debe aceptar el aviso de privacidad antes de abrir tickets.</summary>
    public async Task<HttpClient> CustomerWithConsentAsync(string email = "cliente")
    {
        var client = await ClientAsAsync(email);
        var accept = await client.PostAsJsonAsync("/api/v1/auth/privacy-consent", new PrivacyConsentRequest { Version = Contracts.Common.PrivacyNotice.CurrentVersion }, Json);
        accept.EnsureSuccessStatusCode();

        var fullEmail = email.Contains('@', StringComparison.Ordinal) ? email : $"{email}@{DemoDomain}";
        await _tokenLock.WaitAsync();
        try
        {
            _tokens.Remove(fullEmail);
        }
        finally
        {
            _tokenLock.Release();
        }

        return await ClientAsAsync(email);
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        Nlp.Stop();
        Nlp.Dispose();
        await _postgres.DisposeAsync();
        _tokenLock.Dispose();
        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

public sealed class HelpdeskApiFactory(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
