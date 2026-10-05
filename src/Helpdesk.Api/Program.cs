using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Helpdesk.Api.Errors;
using Helpdesk.Api.Infrastructure;
using Helpdesk.Api.Realtime;
using Helpdesk.Api.Security;
using Helpdesk.Application;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Sla;
using Helpdesk.Contracts.Common;
using Helpdesk.Infrastructure;
using Helpdesk.Infrastructure.Identity;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

if (args.Contains("--healthcheck"))
{
    return await HealthCheckCommand.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
});

builder.Services.Configure<HelpdeskOptions>(builder.Configuration.GetSection(HelpdeskOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = jwt.Value.CreateValidationParameters();
        bearer.Events = new JwtBearerEvents
        {
            // Los navegadores no envían Authorization en WebSockets: SignalR manda el token en access_token.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/" + ApiRoutes.Hub))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(Policies.Configure);

builder.Services.AddRateLimiter(options => RateLimitPolicies.Configure(options, builder.Configuration));

var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (allowedOrigins.Contains("*"))
{
    throw new InvalidOperationException("Cors__AllowedOrigins no admite '*': indica los orígenes explícitos del front.");
}

builder.Services.AddCors(cors => cors.AddPolicy("frontend", policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithHeaders("Authorization", "Content-Type", "If-Match", "X-Requested-With", "X-SignalR-User-Agent")
    .WithExposedHeaders("ETag", "Retry-After", "Content-Disposition")));

builder.Services
    .AddSignalR(hub =>
    {
        hub.MaximumReceiveMessageSize = 32 * 1024;
        hub.EnableDetailedErrors = false;
    })
    .AddJsonProtocol(json => ConfigureJson(json.PayloadSerializerOptions));
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();

builder.Services
    .AddControllers(mvc =>
    {
        mvc.Filters.Add<ValidationFilter>();
        mvc.ModelBinderProviders.Insert(0, new SnakeCaseEnumModelBinderProvider());
    })
    .AddJsonOptions(json => ConfigureJson(json.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(json => ConfigureJson(json.SerializerOptions));
builder.Services.AddProblemDetails(problem => problem.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Instance = null;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddOpenApi("v1", openApi => openApi.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddHealthChecks().AddDbContextCheck<HelpdeskDbContext>("database", tags: ["ready"]);

if (builder.Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
{
    // Solo detrás de un proxy que reescribe X-Forwarded-For (Render, Azure): así el rate limiting ve la IP real.
    builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        forwarded.ForwardLimit = 1;
    });
}

var app = builder.Build();

if (app.Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
{
    app.UseForwardedHeaders();
}

// Se registra fuera del manejador de errores para ver el estado final (401, 404…) y no la excepción interna.
// RequestPath no incluye el query string: el access_token de SignalR nunca llega a los logs.
app.UseSerilogRequestLogging(logging => logging.MessageTemplate = "HTTP {RequestMethod} {RequestPath} → {StatusCode} en {Elapsed:0.0} ms");
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationsHub>("/" + ApiRoutes.Hub);
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous().DisableRateLimiting();
if (app.Configuration.GetValue("OpenApi:Enabled", true))
{
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
    app.MapScalarApiReference("/docs", scalar => scalar
        .WithTitle("helpdesk-net API")
        .WithOpenApiRoutePattern("/openapi/{documentName}.json")).AllowAnonymous();
}

await app.Services.InitializeDatabaseAsync();
await app.RunAsync();
return 0;

static void ConfigureJson(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
}

/// <summary>Punto de entrada visible para WebApplicationFactory en las pruebas de integración.</summary>
public partial class Program
{
    protected Program()
    {
    }
}
