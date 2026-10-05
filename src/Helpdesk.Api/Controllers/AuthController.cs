using Helpdesk.Api.Infrastructure;
using Helpdesk.Api.Security;
using Helpdesk.Application.Abstractions;
using Helpdesk.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Helpdesk.Api.Controllers;

/// <summary>Inicio de sesión de usuarios, rotación de sesión y tokens para clientes de integración.</summary>
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth) : ApiControllerBase
{
    /// <summary>Inicia sesión. Limitado a 5 intentos por minuto e IP; la cuenta se bloquea 15 min tras 5 fallos.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<TokenResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        auth.LoginAsync(request, cancellationToken);

    /// <summary>Canjea el refresh token por un par nuevo (el anterior queda revocado).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    public Task<TokenResponse> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        auth.RefreshAsync(request.RefreshToken, cancellationToken);

    /// <summary>Cierra la sesión revocando toda la familia del refresh token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    /// <summary>Client credentials para servicios (p. ej. el clasificador de IA). Devuelve un JWT con rol Integration y scopes.</summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.ClientToken)]
    [ProducesResponseType(typeof(ClientTokenResponse), StatusCodes.Status200OK)]
    public Task<ClientTokenResponse> ClientToken(ClientTokenRequest request, CancellationToken cancellationToken) =>
        auth.IssueClientTokenAsync(request, cancellationToken);

    [HttpGet("me")]
    [Authorize(Policy = Policies.User)]
    public Task<CurrentUserDto> Me(CancellationToken cancellationToken) => auth.GetCurrentAsync(cancellationToken);

    /// <summary>El cliente acepta el aviso de privacidad (LOPDP). Después debe renovar el token con /refresh.</summary>
    [HttpPost("privacy-consent")]
    [Authorize(Policy = Policies.Customer)]
    public Task<CurrentUserDto> AcceptPrivacy(PrivacyConsentRequest request, CancellationToken cancellationToken) =>
        auth.AcceptPrivacyNoticeAsync(request.Version, cancellationToken);
}
