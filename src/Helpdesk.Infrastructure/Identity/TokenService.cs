using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Helpdesk.Infrastructure.Identity;

/// <summary>
/// Configuración JWT. La clave de firma llega solo por variable de entorno (<c>Jwt__SigningKey</c>) y debe tener
/// al menos 256 bits: si falta o es corta, la aplicación no arranca.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = "helpdesk-net";

    [Required]
    public string Audience { get; set; } = "helpdesk-net-api";

    [Required(ErrorMessage = "Define Jwt__SigningKey (mínimo 32 bytes aleatorios).")]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 24)]
    public int RefreshTokenHours { get; set; } = 8;

    [Range(1, 60)]
    public int ClientTokenMinutes { get; set; } = 15;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Name,
        RoleClaimType = HelpdeskClaims.Role,
    };
}

internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options) =>
        Encoding.UTF8.GetByteCount(options.SigningKey ?? string.Empty) < JwtOptions.MinimumKeyBytes
            ? ValidateOptionsResult.Fail($"Jwt__SigningKey debe tener al menos {JwtOptions.MinimumKeyBytes} bytes (256 bits).")
            : ValidateOptionsResult.Success;
}

/// <summary>Claims propios del token.</summary>
public static class HelpdeskClaims
{
    public const string Role = "role";
    public const string CustomerId = "customer_id";
    public const string Scope = "scope";
    public const string ClientId = "client_id";
    public const string PrivacyNotice = "privacy";
}

/// <summary>Emite JWT HS256 de vida corta y refresh tokens opacos de 256 bits.</summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTimeOffset ExpiresAt) CreateUserToken(AppUser user, string role)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(HelpdeskClaims.Role, role),
        };
        if (user.CustomerId is { } customerId)
        {
            claims.Add(new Claim(HelpdeskClaims.CustomerId, customerId.ToString()));
        }

        if (!string.IsNullOrEmpty(user.PrivacyNoticeVersion))
        {
            claims.Add(new Claim(HelpdeskClaims.PrivacyNotice, user.PrivacyNoticeVersion));
        }

        return Create(claims, options.Value.AccessTokenMinutes);
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateClientToken(ApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, client.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, client.DisplayName),
            new(HelpdeskClaims.ClientId, client.ClientId),
            new(HelpdeskClaims.Role, RoleNames.Integration),
            new(HelpdeskClaims.Scope, string.Join(' ', client.Scopes)),
        ];
        return Create(claims, options.Value.ClientTokenMinutes);
    }

    /// <summary>Token opaco aleatorio (32 bytes, base64url). Solo se persiste su hash.</summary>
    public static string NewRefreshToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private (string Token, DateTimeOffset ExpiresAt) Create(IEnumerable<Claim> claims, int minutes)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(minutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            Subject = new ClaimsIdentity([.. claims, new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(options.Value.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };
        return (_handler.CreateToken(descriptor), expires);
    }
}
