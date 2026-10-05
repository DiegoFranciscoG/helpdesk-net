using Helpdesk.Contracts.Auth;
using Helpdesk.Contracts.Catalog;

namespace Helpdesk.Application.Abstractions;

/// <summary>Autenticación de usuarios (JWT + refresh token rotado) y de clientes de integración.</summary>
public interface IAuthService
{
    Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    Task<ClientTokenResponse> IssueClientTokenAsync(ClientTokenRequest request, CancellationToken cancellationToken);

    Task<CurrentUserDto> GetCurrentAsync(CancellationToken cancellationToken);

    Task<CurrentUserDto> AcceptPrivacyNoticeAsync(string version, CancellationToken cancellationToken);
}

/// <summary>Administración de usuarios (solo rol Admin).</summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken);

    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Derecho de eliminación (LOPDP art. 15): seudonimiza y desactiva los usuarios del portal de un cliente.</summary>
    Task<IReadOnlyList<Guid>> AnonymizeCustomerUsersAsync(Guid customerId, CancellationToken cancellationToken);
}
