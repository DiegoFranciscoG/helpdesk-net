using System.Text.Json;
using System.Text.Json.Serialization;
using Helpdesk.Contracts.Common;
using Helpdesk.Contracts.Notifications;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Services;

/// <summary>
/// Conexión SignalR con reconexión automática. El servidor solo envía identificadores y estados;
/// las pantallas vuelven a pedir los datos a la API (que aplica la autorización).
/// </summary>
public sealed class RealtimeClient(SessionService session, IOptions<ApiOptions> options) : IAsyncDisposable
{
    private HubConnection? _connection;

    public event Action<TicketChangedMessage>? TicketChanged;

    public event Action<NotificationDto>? NotificationReceived;

    public event Action<HubConnectionState>? StateChanged;

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public async Task StartAsync()
    {
        if (_connection is not null)
        {
            return;
        }

        var url = new Uri(new Uri(options.Value.BaseUrl.TrimEnd('/') + "/"), ApiRoutes.Hub);
        _connection = new HubConnectionBuilder()
            .WithUrl(url, http => http.AccessTokenProvider = () => session.GetAccessTokenAsync())
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)])
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)))
            .Build();

        _connection.On<TicketChangedMessage>(HubMethods.TicketChanged, message => TicketChanged?.Invoke(message));
        _connection.On<NotificationDto>(HubMethods.NotificationReceived, notification => NotificationReceived?.Invoke(notification));
        _connection.Reconnecting += _ => Notify();
        _connection.Reconnected += _ => Notify();
        _connection.Closed += _ => Notify();

        try
        {
            await _connection.StartAsync();
        }
        catch (HttpRequestException)
        {
            // Sin conexión al hub la app sigue funcionando; la reconexión automática lo reintentará.
        }

        await Notify();
    }

    public async Task StopAsync()
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.DisposeAsync();
        _connection = null;
        await Notify();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private Task Notify()
    {
        StateChanged?.Invoke(State);
        return Task.CompletedTask;
    }
}
