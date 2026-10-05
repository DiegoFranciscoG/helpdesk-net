namespace Helpdesk.Contracts.Auth;

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class ClientTokenRequest
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class PrivacyConsentRequest
{
    public string Version { get; set; } = string.Empty;
}

/// <summary>Usuario autenticado tal como lo necesita el front.</summary>
public sealed record CurrentUserDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    Guid? CustomerId,
    IReadOnlyList<Guid> TeamIds,
    bool PrivacyNoticeAccepted,
    string? PrivacyNoticeVersion);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserDto User);

public sealed record ClientTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, IReadOnlyList<string> Scopes);
