using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Exceptions;
using Helpdesk.Contracts.Auth;
using Helpdesk.Contracts.Common;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helpdesk.Infrastructure.Identity;

/// <summary>
/// Inicio de sesión y sesiones. Errores siempre genéricos (no revela si el correo existe), bloqueo tras 5 fallos,
/// refresh token rotado en cada uso y revocación de la familia completa si se reutiliza uno ya rotado (RFC 9700).
/// </summary>
internal sealed partial class AuthService(
    UserManager<AppUser> userManager,
    HelpdeskDbContext db,
    TokenService tokens,
    IPasswordHasher<ApiClient> clientHasher,
    ICurrentUser currentUser,
    IOptions<JwtOptions> options,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString()));

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Igualar el tiempo de respuesta: verificar contra un hash ficticio evita enumerar correos por latencia.
            userManager.PasswordHasher.VerifyHashedPassword(new AppUser(), DummyHash.Value, request.Password);
            LogLoginFailed(logger, "unknown_user");
            throw new AuthenticationFailedException();
        }

        if (!user.IsActive || await userManager.IsLockedOutAsync(user))
        {
            LogLoginFailed(logger, user.IsActive ? "locked_out" : "inactive");
            throw new AuthenticationFailedException();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            LogLoginFailed(logger, "bad_password");
            throw new AuthenticationFailedException();
        }

        // Sentencia atómica en vez de UserManager.UpdateAsync: dos inicios de sesión simultáneos del mismo usuario
        // (dos pestañas) no compiten por el ConcurrencyStamp de Identity.
        var now = clock.GetUtcNow();
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AccessFailedCount, 0).SetProperty(u => u.LastLoginAt, now), cancellationToken);

        // Expiración absoluta truncada a segundos: PostgreSQL guarda microsegundos y el valor debe ser idéntico en cada rotación.
        var familyExpiresAt = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero).AddHours(options.Value.RefreshTokenHours);
        return await IssueAsync(user, Guid.CreateVersion7(), familyExpiresAt, cancellationToken);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == TokenService.Hash(refreshToken), cancellationToken)
            ?? throw new AuthenticationFailedException("Sesión inválida.");

        if (stored.RevokedAt is not null)
        {
            // Reutilización de un token ya rotado: posible robo. Se revoca toda la familia.
            await RevokeFamilyAsync(stored.FamilyId, "reuse_detected", now, cancellationToken);
            LogRefreshReuse(logger, stored.UserId);
            throw new AuthenticationFailedException("Sesión inválida.");
        }

        if (stored.ExpiresAt <= now)
        {
            throw new AuthenticationFailedException("La sesión expiró. Inicia sesión otra vez.");
        }

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            await RevokeFamilyAsync(stored.FamilyId, "user_inactive", now, cancellationToken);
            throw new AuthenticationFailedException("Sesión inválida.");
        }

        var response = await IssueAsync(user, stored.FamilyId, stored.ExpiresAt, cancellationToken, previous: stored);
        return response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == TokenService.Hash(refreshToken), cancellationToken);
        if (stored is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, "logout", clock.GetUtcNow(), cancellationToken);
        }
    }

    public async Task<ClientTokenResponse> IssueClientTokenAsync(ClientTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.ClientId == request.ClientId && c.IsActive, cancellationToken);
        if (client is null || clientHasher.VerifyHashedPassword(client, client.SecretHash, request.ClientSecret) == PasswordVerificationResult.Failed)
        {
            LogLoginFailed(logger, "client_credentials");
            throw new AuthenticationFailedException();
        }

        client.MarkUsed(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        var (token, expires) = tokens.CreateClientToken(client);
        return new ClientTokenResponse(token, expires, client.Scopes);
    }

    public async Task<CurrentUserDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(currentUser.UserId.ToString()) ?? throw new AuthenticationFailedException();
        return await ToDtoAsync(user, cancellationToken);
    }

    public async Task<CurrentUserDto> AcceptPrivacyNoticeAsync(string version, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(currentUser.UserId.ToString()) ?? throw new AuthenticationFailedException();
        if (version != PrivacyNotice.CurrentVersion)
        {
            throw new BusinessRuleException("privacy_version", "Versión del aviso de privacidad no vigente.");
        }

        // Idempotente y atómico: aceptar desde dos pestañas a la vez no produce un conflicto de concurrencia.
        var now = clock.GetUtcNow();
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PrivacyNoticeVersion, version).SetProperty(u => u.PrivacyNoticeAcceptedAt, now), cancellationToken);
        await db.Entry(user).ReloadAsync(cancellationToken);
        return await ToDtoAsync(user, cancellationToken);
    }

    private async Task<TokenResponse> IssueAsync(AppUser user, Guid familyId, DateTimeOffset familyExpiresAt, CancellationToken cancellationToken, RefreshToken? previous = null)
    {
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault()
            ?? throw new AuthenticationFailedException();
        var (accessToken, accessExpires) = tokens.CreateUserToken(user, role);

        var plainRefresh = TokenService.NewRefreshToken();
        var refresh = new RefreshToken(user.Id, TokenService.Hash(plainRefresh), familyId, clock.GetUtcNow(), familyExpiresAt);
        db.RefreshTokens.Add(refresh);
        previous?.Revoke("rotated", clock.GetUtcNow(), refresh.Id);
        await db.SaveChangesAsync(cancellationToken);

        return new TokenResponse(accessToken, accessExpires, plainRefresh, familyExpiresAt, await ToDtoAsync(user, cancellationToken, role));
    }

    private async Task<CurrentUserDto> ToDtoAsync(AppUser user, CancellationToken cancellationToken, string? role = null)
    {
        role ??= (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        var teams = await db.TeamMembers.AsNoTracking().Where(m => m.UserId == user.Id).Select(m => m.TeamId).ToListAsync(cancellationToken);
        var accepted = user.PrivacyNoticeVersion == PrivacyNotice.CurrentVersion;
        return new CurrentUserDto(user.Id, user.FullName, user.Email ?? string.Empty, role, user.CustomerId, teams, accepted, user.PrivacyNoticeVersion);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var family = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(cancellationToken);
        family.ForEach(t => t.Revoke(reason, now));
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inicio de sesión fallido ({Reason})")]
    private static partial void LogLoginFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reutilización de refresh token del usuario {UserId}: familia revocada")]
    private static partial void LogRefreshReuse(ILogger logger, Guid userId);
}
