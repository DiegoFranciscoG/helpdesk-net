using Helpdesk.Application.Abstractions;
using Helpdesk.Contracts.Validation;
using Helpdesk.Infrastructure.Background;
using Helpdesk.Infrastructure.Identity;
using Helpdesk.Infrastructure.Nlp;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Persistence.Seed;
using Helpdesk.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Helpdesk.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Helpdesk";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Define ConnectionStrings__{ConnectionStringName} (cadena de conexión de PostgreSQL).");
        }

        services.AddDbContext<HelpdeskDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                .EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention());

        AddIdentity(services, configuration);
        AddPersistence(services);

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        AddNlp(services, configuration);
        AddWorkers(services, configuration);
        return services;
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 220_000);
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = PasswordRules.MinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<HelpdeskDbContext>()
            .AddPasswordValidator<CommonPasswordValidator>()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>();

        services.AddSingleton<IPasswordHasher<ApiClient>, PasswordHasher<ApiClient>>();
        services.AddSingleton<TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<ISuggestionRepository, SuggestionRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISlaRepository, SlaRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<ITicketQueries, TicketQueries>();
        services.AddScoped<DemoDataSeeder>();
    }

    private static void AddNlp(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(NlpOptions.SectionName);
        var nlp = section.Get<NlpOptions>() ?? new NlpOptions();
        if (!string.IsNullOrWhiteSpace(nlp.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(nlp.ApiKey))
            {
                throw new InvalidOperationException("Nlp__BaseUrl está definido pero falta Nlp__ApiKey.");
            }

            if (!Uri.TryCreate(nlp.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException("Nlp__BaseUrl debe ser una URL http(s) absoluta.");
            }
        }

        services.Configure<NlpOptions>(section);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(nlp.TimeoutSeconds, 5, 120));
        services
            .AddHttpClient<INlpClassifier, NlpTicketsClient>(client =>
            {
                if (!string.IsNullOrWhiteSpace(nlp.BaseUrl))
                {
                    client.BaseAddress = new Uri(nlp.BaseUrl.TrimEnd('/') + "/");
                }

                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = 256 * 1024;
            })
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = timeout;
                options.TotalRequestTimeout.Timeout = timeout + TimeSpan.FromSeconds(5);
                options.CircuitBreaker.SamplingDuration = timeout * 2;
                // POST /classify con persist=true no es idempotente: el reintento lo hace el worker, no el handler.
                options.Retry.DisableForUnsafeHttpMethods();
            });
    }

    private static void AddWorkers(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        services.AddSingleton<ISuggestionInbox, ChannelSuggestionInbox>();
        var workers = configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions();
        if (workers.Enabled)
        {
            services.AddHostedService<SlaMonitorWorker>();
            services.AddHostedService<SuggestionWorker>();
        }
    }
}
