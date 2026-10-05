using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.IntegrationTests.Infrastructure;

internal static class HttpExtensions
{
    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: HelpdeskFixture.Json) };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, HelpdeskFixture.Json);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Se esperaba {(int)expected} y llegó {(int)response.StatusCode}: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(HelpdeskFixture.Json))!;
    }

    public static async Task<string> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString()! : string.Empty;
    }

    public static async Task<TicketDetailDto> CreateTicketAsync(this HttpClient client, string title = "No puedo ingresar a la banca móvil", Impact impact = Impact.Medium, Urgency urgency = Urgency.Medium, Guid? categoryId = null, Guid? customerId = null, TicketChannel channel = TicketChannel.Portal)
    {
        var request = new CreateTicketRequest
        {
            Title = title,
            Description = "Desde esta mañana la aplicación muestra un error al iniciar sesión.",
            Impact = impact,
            Urgency = urgency,
            CategoryId = categoryId,
            CustomerId = customerId,
            Channel = channel,
        };
        return await (await client.PostJsonAsync("/api/v1/tickets", request)).ReadAsync<TicketDetailDto>(HttpStatusCode.Created);
    }

    public static async Task<TicketDetailDto> GetTicketAsync(this HttpClient client, Guid id) =>
        await (await client.GetAsync($"/api/v1/tickets/{id}")).ReadAsync<TicketDetailDto>();

    public static async Task<TicketDetailDto> TransitionAsync(this HttpClient client, Guid id, TransitionAction action, string? reason = null, string? summary = null) =>
        await (await client.PostJsonAsync($"/api/v1/tickets/{id}/transitions", new TransitionRequest { Action = action, Reason = reason, ResolutionSummary = summary })).ReadAsync<TicketDetailDto>();
}
