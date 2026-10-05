using Microsoft.AspNetCore.Identity;

namespace Helpdesk.Infrastructure.Identity;

/// <summary>Usuario de ASP.NET Core Identity con los datos mínimos que necesita la mesa de ayuda.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int FullNameMaxLength = 120;

    public AppUser()
    {
        Id = Guid.CreateVersion7();
        FullName = string.Empty;
    }

    public string FullName { get; set; }

    /// <summary>Cliente al que pertenece un usuario del portal; null para el personal interno.</summary>
    public Guid? CustomerId { get; set; }

    public bool IsActive { get; set; } = true;

    public string? PrivacyNoticeVersion { get; set; }

    public DateTimeOffset? PrivacyNoticeAcceptedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
        Id = Guid.CreateVersion7();
    }

    public AppRole(string roleName)
        : base(roleName)
    {
        Id = Guid.CreateVersion7();
    }
}

/// <summary>Nombres de rol estables (se usan en políticas y en el token).</summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Supervisor = "Supervisor";
    public const string Agent = "Agent";
    public const string Customer = "Customer";
    public const string Integration = "Integration";

    public static IReadOnlyList<string> UserRoles { get; } = [Admin, Supervisor, Agent, Customer];
}

/// <summary>
/// Refresh token opaco. Solo se guarda su SHA-256; se rota en cada uso y la reutilización de uno ya rotado
/// revoca toda la familia (RFC 9700 §4.14).
/// </summary>
public sealed class RefreshToken
{
    public const string RotatedReason = "rotated";

    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public RefreshToken(Guid userId, string tokenHash, Guid familyId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(string reason, DateTimeOffset now, Guid? replacedById = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
        ReplacedById = replacedById;
    }
}

/// <summary>Cliente de integración (p. ej. el servicio de IA) que obtiene JWT con client credentials.</summary>
public sealed class ApiClient
{
    private ApiClient()
    {
        ClientId = string.Empty;
        DisplayName = string.Empty;
        SecretHash = string.Empty;
        Scopes = [];
    }

    public ApiClient(string clientId, string displayName, string secretHash, string[] scopes, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        ClientId = clientId;
        DisplayName = displayName;
        SecretHash = secretHash;
        Scopes = scopes;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string ClientId { get; private set; }

    public string DisplayName { get; private set; }

    public string SecretHash { get; private set; }

    public string[] Scopes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public void RotateSecret(string secretHash, string[] scopes)
    {
        SecretHash = secretHash;
        Scopes = scopes;
        IsActive = true;
    }

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;
}

/// <summary>Alcances (scopes) que puede tener un cliente de integración.</summary>
public static class ApiScopes
{
    public const string TicketsRead = "tickets:read";
    public const string SuggestionsWrite = "suggestions:write";

    public static IReadOnlyList<string> All { get; } = [TicketsRead, SuggestionsWrite];
}
