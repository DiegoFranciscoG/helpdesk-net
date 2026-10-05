using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Helpdesk.Contracts.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Helpdesk.Web.Services;

/// <summary>Opciones públicas del front (no hay secretos en el navegador: solo la URL de la API).</summary>
public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5080";
}

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

/// <summary>
/// Sesión del usuario. El access token (15 min) vive solo en memoria; el refresh token, rotado en cada uso, se guarda
/// en sessionStorage para sobrevivir a una recarga y desaparece al cerrar la pestaña (RFC 10017, guía de Microsoft
/// para Blazor WebAssembly).
/// </summary>
public sealed class SessionService(IHttpClientFactory httpClientFactory, IJSRuntime js) : IDisposable
{
    public const string AnonymousClient = "anonymous";
    private const string RefreshKey = "helpdesk.refresh";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessExpiresAt;

    public event Action? Changed;

    public CurrentUserDto? User { get; private set; }

    public bool IsAuthenticated => User is not null;

    public bool IsStaff => User?.Role is "Admin" or "Supervisor" or "Agent";

    public bool IsSupervisor => User?.Role is "Admin" or "Supervisor";

    public bool IsAdmin => User?.Role == "Admin";

    public bool IsCustomer => User?.Role == "Customer";

    /// <summary>Al cargar la app intenta recuperar la sesión con el refresh token guardado.</summary>
    public async Task RestoreAsync()
    {
        var refresh = await js.InvokeAsync<string?>("helpdesk.session.get", RefreshKey);
        if (!string.IsNullOrEmpty(refresh))
        {
            await RefreshAsync(refresh);
        }
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        using var response = await Anonymous().PostAsJsonAsync("api/v1/auth/login", new LoginRequest { Email = email, Password = password }, JsonDefaults.Options);
        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                ? "Demasiados intentos. Espera un minuto e inténtalo otra vez."
                : "Correo o contraseña incorrectos.";
        }

        await StoreAsync((await response.Content.ReadFromJsonAsync<TokenResponse>(JsonDefaults.Options))!);
        return null;
    }

    public async Task LogoutAsync()
    {
        var refresh = await js.InvokeAsync<string?>("helpdesk.session.get", RefreshKey);
        if (!string.IsNullOrEmpty(refresh))
        {
            try
            {
                using var _ = await Anonymous().PostAsJsonAsync("api/v1/auth/logout", new RefreshRequest { RefreshToken = refresh }, JsonDefaults.Options);
            }
            catch (HttpRequestException)
            {
                // Si la API no responde, igual se borra la sesión local.
            }
        }

        await ClearAsync();
    }

    /// <summary>Token vigente para la API y SignalR; se renueva solo si está por vencer.</summary>
    public async Task<string?> GetAccessTokenAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _accessToken is not null && _accessExpiresAt - RenewBefore > DateTimeOffset.UtcNow)
        {
            return _accessToken;
        }

        var refresh = await js.InvokeAsync<string?>("helpdesk.session.get", RefreshKey);
        return string.IsNullOrEmpty(refresh) ? null : await RefreshAsync(refresh);
    }

    /// <summary>Tras aceptar el aviso de privacidad se pide un token nuevo que ya trae ese dato.</summary>
    public Task<string?> ReloadAsync() => GetAccessTokenAsync(forceRefresh: true);

    private async Task<string?> RefreshAsync(string refresh)
    {
        await _lock.WaitAsync();
        try
        {
            // Otra llamada pudo renovar mientras esperábamos el candado.
            var stored = await js.InvokeAsync<string?>("helpdesk.session.get", RefreshKey);
            if (stored != refresh && _accessToken is not null && _accessExpiresAt - RenewBefore > DateTimeOffset.UtcNow)
            {
                return _accessToken;
            }

            using var response = await Anonymous().PostAsJsonAsync("api/v1/auth/refresh", new RefreshRequest { RefreshToken = stored ?? refresh }, JsonDefaults.Options);
            if (!response.IsSuccessStatusCode)
            {
                await ClearAsync();
                return null;
            }

            var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>(JsonDefaults.Options))!;
            await StoreAsync(tokens);
            return tokens.AccessToken;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task StoreAsync(TokenResponse tokens)
    {
        _accessToken = tokens.AccessToken;
        _accessExpiresAt = tokens.AccessTokenExpiresAt;
        User = tokens.User;
        await js.InvokeVoidAsync("helpdesk.session.set", RefreshKey, tokens.RefreshToken);
        Changed?.Invoke();
    }

    private async Task ClearAsync()
    {
        _accessToken = null;
        User = null;
        await js.InvokeVoidAsync("helpdesk.session.remove", RefreshKey);
        Changed?.Invoke();
    }

    public void Dispose() => _lock.Dispose();

    private HttpClient Anonymous() => httpClientFactory.CreateClient(AnonymousClient);
}

/// <summary>Estado de autenticación para AuthorizeView. La autorización real la hace siempre la API.</summary>
public sealed class HelpdeskAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly SessionService _session;

    public HelpdeskAuthStateProvider(SessionService session)
    {
        _session = session;
        _session.Changed += OnChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _session.User;
        if (user is null)
        {
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.FullName), new Claim(ClaimTypes.Role, user.Role)],
            authenticationType: "jwt");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public void Dispose() => _session.Changed -= OnChanged;

    private void OnChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}

/// <summary>Agrega el Bearer a cada llamada y, ante un 401, renueva la sesión una vez y reintenta.</summary>
public sealed class AuthorizingHandler(SessionService session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await session.GetAccessTokenAsync();
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized || request.Content is MultipartFormDataContent)
        {
            return response;
        }

        var renewed = await session.GetAccessTokenAsync(forceRefresh: true);
        if (renewed is null)
        {
            return response;
        }

        response.Dispose();
        using var retry = await CloneAsync(request);
        retry.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", renewed);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
