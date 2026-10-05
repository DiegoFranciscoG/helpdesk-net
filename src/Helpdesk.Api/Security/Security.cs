using System.Security.Claims;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Security;
using Helpdesk.Domain.Common;
using Helpdesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Helpdesk.Api.Security;

/// <summary>Nombres de las políticas de autorización (función/rol, OWASP API5).</summary>
public static class Policies
{
    public const string User = "user";
    public const string Staff = "staff";
    public const string Supervisor = "supervisor";
    public const string Admin = "admin";
    public const string Customer = "customer";
    public const string IntegrationRead = "integration:tickets:read";
    public const string IntegrationWrite = "integration:suggestions:write";

    public static void Configure(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var user = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(RoleNames.Admin, RoleNames.Supervisor, RoleNames.Agent, RoleNames.Customer)
            .Build();

        // Deny-by-default: todo endpoint sin atributo exige un usuario interactivo (no un cliente de integración).
        options.DefaultPolicy = user;
        options.FallbackPolicy = user;
        options.AddPolicy(User, user);
        options.AddPolicy(Staff, p => p.RequireAuthenticatedUser().RequireRole(RoleNames.Admin, RoleNames.Supervisor, RoleNames.Agent));
        options.AddPolicy(Supervisor, p => p.RequireAuthenticatedUser().RequireRole(RoleNames.Admin, RoleNames.Supervisor));
        options.AddPolicy(Admin, p => p.RequireAuthenticatedUser().RequireRole(RoleNames.Admin));
        options.AddPolicy(Customer, p => p.RequireAuthenticatedUser().RequireRole(RoleNames.Customer));
        options.AddPolicy(IntegrationRead, p => p.RequireAuthenticatedUser().RequireRole(RoleNames.Integration).RequireAssertion(c => HasScope(c.User, ApiScopes.TicketsRead)));
        options.AddPolicy(IntegrationWrite, p => p.RequireAuthenticatedUser().RequireRole(RoleNames.Integration).RequireAssertion(c => HasScope(c.User, ApiScopes.SuggestionsWrite)));
    }

    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindFirst(HelpdeskClaims.Scope)?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal) == true;
}

/// <summary>Usuario actual leído del JWT. Fuera de una petición (workers) se comporta como el sistema.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, IUserDirectory users) : ICurrentUser
{
    private AccessContext? _access;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : Guid.Empty;

    public ActorRole Role => IsAuthenticated && Enum.TryParse<ActorRole>(Principal!.FindFirst(HelpdeskClaims.Role)?.Value, out var role) ? role : ActorRole.System;

    public Guid? CustomerId => Guid.TryParse(Principal?.FindFirst(HelpdeskClaims.CustomerId)?.Value, out var id) ? id : null;

    public IReadOnlyCollection<string> Scopes =>
        Principal?.FindFirst(HelpdeskClaims.Scope)?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    public string? PrivacyNoticeVersion => Principal?.FindFirst(HelpdeskClaims.PrivacyNotice)?.Value;

    public Actor Actor => IsAuthenticated ? new Actor(UserId, Role) : Actor.SystemActor;

    public async Task<AccessContext> GetAccessAsync(CancellationToken cancellationToken)
    {
        if (_access is not null)
        {
            return _access;
        }

        var role = Role;
        IReadOnlySet<Guid> teams = role is ActorRole.Admin or ActorRole.Supervisor or ActorRole.Agent
            ? await users.GetTeamIdsAsync(UserId, cancellationToken)
            : new HashSet<Guid>();
        _access = new AccessContext(UserId, role, CustomerId, teams);
        return _access;
    }
}

/// <summary>SignalR identifica a cada conexión por el claim <c>sub</c> del token.</summary>
internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection?.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}

/// <summary>
/// Cabeceras de seguridad (OWASP Secure Headers). La API solo devuelve JSON, así que su CSP es la más estricta;
/// la referencia interactiva de OpenAPI (<c>/docs</c>) necesita una política propia.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    private const string DocsPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; font-src 'self' data: https://fonts.gstatic.com https://cdn.jsdelivr.net; img-src 'self' data: https:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";
            var isDocs = context.Request.Path.StartsWithSegments("/docs") || context.Request.Path.StartsWithSegments("/scalar");
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers.ContentSecurityPolicy = isDocs ? DocsPolicy : ApiPolicy;
            }

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });
        return next(context);
    }
}
