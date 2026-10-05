using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Helpdesk.Api.Security;

/// <summary>
/// Límites de consumo (OWASP API4), configurables en <c>RateLimiting:*</c>: login y client credentials 5/min por IP
/// (particiones separadas), renovación de sesión 30/min, subidas 10/min por usuario y un límite global de 300/min
/// por usuario o IP. Responde 429 con Retry-After.
/// </summary>
public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Refresh = "refresh";
    public const string Uploads = "uploads";
    public const string ClientToken = "client-token";

    public static void Configure(RateLimiterOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        var limits = configuration.GetSection("RateLimiting");
        var authPerMinute = limits.GetValue("AuthPerMinute", 5);
        var refreshPerMinute = limits.GetValue("RefreshPerMinute", 30);
        var uploadsPerMinute = limits.GetValue("UploadsPerMinute", 10);
        var globalPerMinute = limits.GetValue("GlobalPerMinute", 300);
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Demasiadas solicitudes",
                Detail = "Espera un momento antes de volver a intentar.",
            };
            problem.Extensions["code"] = "rate_limited";
            await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        };

        options.AddPolicy(Auth, context => RateLimitPartition.GetFixedWindowLimiter(
            "auth:" + ClientIp(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        options.AddPolicy(ClientToken, context => RateLimitPartition.GetFixedWindowLimiter(
            "token:" + ClientIp(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        options.AddPolicy(Refresh, context => RateLimitPartition.GetFixedWindowLimiter(
            "refresh:" + ClientIp(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = refreshPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        options.AddPolicy(Uploads, context => RateLimitPartition.GetFixedWindowLimiter(
            "uploads:" + UserOrIp(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = uploadsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            context.Request.Path.StartsWithSegments("/hubs")
                ? RateLimitPartition.GetNoLimiter("hubs")
                : RateLimitPartition.GetTokenBucketLimiter("global:" + UserOrIp(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = globalPerMinute,
                    TokensPerPeriod = globalPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    }

    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserOrIp(HttpContext context) =>
        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? ClientIp(context);
}
