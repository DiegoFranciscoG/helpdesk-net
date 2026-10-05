using System.Net;
using System.Text;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Contracts.Reports;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Persistence.Seed;
using Helpdesk.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>Notificaciones en tiempo real por SignalR, autenticadas con el JWT y separadas por cola y cliente.</summary>
public sealed class RealtimeTests(HelpdeskFixture fixture)
{
    [Fact]
    public async Task Queue_members_receive_new_tickets_live_and_other_customers_do_not()
    {
        var agentToken = await fixture.TokenAsync("agente@helpdesk.example");
        var strangerToken = await fixture.TokenAsync("luis.cardenas@correo.example");
        await using var agentHub = Connect(agentToken.AccessToken);
        await using var strangerHub = Connect(strangerToken.AccessToken);

        var title = "Tiempo real " + Guid.NewGuid().ToString("N")[..6];
        var agentMessages = new System.Collections.Concurrent.ConcurrentQueue<TicketChangedMessage>();
        var agentNotifications = new System.Collections.Concurrent.ConcurrentQueue<NotificationDto>();
        var strangerMessages = new System.Collections.Concurrent.ConcurrentQueue<TicketChangedMessage>();
        agentHub.On<TicketChangedMessage>(HubMethods.TicketChanged, agentMessages.Enqueue);
        agentHub.On<NotificationDto>(HubMethods.NotificationReceived, agentNotifications.Enqueue);
        strangerHub.On<TicketChangedMessage>(HubMethods.TicketChanged, strangerMessages.Enqueue);
        await agentHub.StartAsync();
        await strangerHub.StartAsync();

        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync(title: title, impact: Impact.High, urgency: Urgency.High);

        // El evento puede llegar antes de que termine la petición HTTP: se espera sobre lo ya recibido.
        var message = await WaitForAsync(agentMessages, m => m.TicketId == ticket.Id && m.Kind == TicketChangeKind.Created);
        var notification = await WaitForAsync(agentNotifications, n => n.TicketId == ticket.Id);
        message.Priority.Should().Be(Priority.Critical);
        message.Number.Should().Be(ticket.Number);
        notification.Type.Should().Be(Domain.Notifications.NotificationType.TicketCreated);
        await Task.Delay(500);
        strangerMessages.Should().NotContain(m => m.TicketId == ticket.Id);
    }

    private static async Task<T> WaitForAsync<T>(System.Collections.Concurrent.ConcurrentQueue<T> received, Func<T, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var found = received.FirstOrDefault(match);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("No llegó el mensaje esperado por SignalR.");
    }

    [Fact]
    public async Task Hub_rejects_anonymous_connections()
    {
        await using var hub = Connect(token: null);

        var act = () => hub.StartAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private HubConnection Connect(string? token) => new HubConnectionBuilder()
        .WithUrl(new Uri(fixture.Factory.Server.BaseAddress, "/hubs/notifications"), options =>
        {
            options.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
            if (token is not null)
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            }
        })
        .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower)))
        .Build();
}

public sealed class ReportTests(HelpdeskFixture fixture)
{
    [Fact]
    public async Task Overview_is_consistent_with_its_breakdowns()
    {
        var supervisor = await fixture.ClientAsAsync("supervisor");

        var overview = await (await supervisor.GetAsync("/api/v1/reports/overview")).ReadAsync<ReportOverviewDto>();

        overview.Created.Should().BeGreaterThan(0);
        overview.ByPriority.Sum(p => p.Created).Should().Be(overview.Created);
        overview.ByCategory.Sum(c => c.Created).Should().Be(overview.Created);
        overview.Daily.Should().HaveCount(30);
        overview.Daily.Sum(d => d.Created).Should().Be(overview.Created);
        overview.ResolutionCompliancePercent.Should().BeInRange(0, 100);
        overview.CsatAverage.Should().BeInRange(1, 5);
        overview.AverageResolutionBusinessHours.Should().BePositive();
    }

    [Fact]
    public async Task Agent_workload_and_custom_ranges()
    {
        var supervisor = await fixture.ClientAsAsync("supervisor");

        var agents = await (await supervisor.GetAsync("/api/v1/reports/agents?from=2026-08-01&to=2026-10-31")).ReadAsync<List<AgentWorkloadRow>>();
        var invalid = await supervisor.GetAsync("/api/v1/reports/overview?from=2026-10-10&to=2026-10-01");
        var tooLong = await supervisor.GetAsync("/api/v1/reports/overview?from=2024-01-01&to=2026-01-01");

        agents.Should().Contain(a => a.AgentName == "Carlos Vélez");
        agents.Sum(a => a.ResolvedInPeriod).Should().BeGreaterThan(0);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Csv_export_has_no_personal_data()
    {
        var supervisor = await fixture.ClientAsAsync("supervisor");

        var response = await supervisor.GetAsync("/api/v1/reports/tickets.csv");
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        csv.TrimStart('﻿').Should().StartWith("numero,creado,estado,prioridad");
        csv.Should().NotContain("@").And.NotContain("Sofía Mendoza").And.Contain("CLI-0000");
    }

    [Fact]
    public async Task Team_filter_narrows_the_report()
    {
        var supervisor = await fixture.ClientAsAsync("supervisor");

        var all = await (await supervisor.GetAsync("/api/v1/reports/overview")).ReadAsync<ReportOverviewDto>();
        var fraud = await (await supervisor.GetAsync($"/api/v1/reports/overview?teamId={ReferenceData.FraudSecurityTeamId}")).ReadAsync<ReportOverviewDto>();

        fraud.Created.Should().BeLessThan(all.Created);
    }
}
