using Helpdesk.Infrastructure.Identity;
using Helpdesk.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helpdesk.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Aplica migraciones al arrancar (EF Core 9+ toma un bloqueo y evita migraciones concurrentes). Útil en Compose
    /// y en el plan gratuito de Render; en producción se recomienda el bundle o el script idempotente en el despliegue.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    /// <summary>Crea clientes, usuarios y tickets ficticios si la base está vacía.</summary>
    public bool SeedDemoData { get; set; }
}

/// <summary>Arranque de la base: migraciones, administrador inicial, cliente de integración y datos demo.</summary>
public static partial class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var configuration = provider.GetRequiredService<IConfiguration>();
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var db = provider.GetRequiredService<HelpdeskDbContext>();

        if (options.ApplyMigrationsOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
            LogMigrated(logger);
        }

        await SeedBootstrapAdminAsync(provider, configuration, logger);
        await SeedIntegrationClientAsync(provider, configuration, cancellationToken);

        if (options.SeedDemoData)
        {
            await provider.GetRequiredService<DemoDataSeeder>().SeedAsync(cancellationToken);
        }
    }

    /// <summary>Crea el primer administrador desde <c>Bootstrap__AdminEmail</c>/<c>Bootstrap__AdminPassword</c> si no hay ninguno.</summary>
    private static async Task SeedBootstrapAdminAsync(IServiceProvider provider, IConfiguration configuration, ILogger logger)
    {
        var email = configuration["Bootstrap:AdminEmail"];
        var password = configuration["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var users = provider.GetRequiredService<UserManager<AppUser>>();
        if ((await users.GetUsersInRoleAsync(RoleNames.Admin)).Count > 0)
        {
            return;
        }

        var admin = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = "Administración",
            CreatedAt = provider.GetRequiredService<TimeProvider>().GetUtcNow(),
        };
        var result = await users.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("No se pudo crear el administrador inicial: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(admin, RoleNames.Admin);
        LogAdminCreated(logger);
    }

    /// <summary>Registra o actualiza el cliente de integración desde <c>Integration__ClientId</c>/<c>Integration__ClientSecret</c>.</summary>
    private static async Task SeedIntegrationClientAsync(IServiceProvider provider, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var clientId = configuration["Integration:ClientId"];
        var secret = configuration["Integration:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        if (secret.Length < 32)
        {
            throw new InvalidOperationException("Integration__ClientSecret debe tener al menos 32 caracteres.");
        }

        var db = provider.GetRequiredService<HelpdeskDbContext>();
        var hasher = provider.GetRequiredService<IPasswordHasher<ApiClient>>();
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
        if (client is null)
        {
            client = new ApiClient(clientId, "Clasificador de tickets", string.Empty, [.. ApiScopes.All], provider.GetRequiredService<TimeProvider>().GetUtcNow());
            db.ApiClients.Add(client);
        }

        if (client.SecretHash.Length == 0 || hasher.VerifyHashedPassword(client, client.SecretHash, secret) == PasswordVerificationResult.Failed)
        {
            client.RotateSecret(hasher.HashPassword(client, secret), [.. ApiScopes.All]);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migraciones aplicadas")]
    private static partial void LogMigrated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Administrador inicial creado")]
    private static partial void LogAdminCreated(ILogger logger);
}
