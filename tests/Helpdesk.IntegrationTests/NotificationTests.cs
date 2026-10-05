using System.Net;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Contracts.Tickets;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public sealed class NotificationTests(HelpdeskFixture fixture)
{
    [Fact]
    public async Task Customer_is_notified_of_replies_and_manages_their_own_notifications()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var ticket = await customer.CreateTicketAsync();
        await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new CreateCommentRequest { Body = "Estamos revisando su caso." });

        var unread = await (await customer.GetAsync("/api/v1/notifications?unreadOnly=true")).ReadAsync<List<NotificationDto>>();
        var reply = unread.Should().Contain(n => n.TicketId == ticket.Id && n.Type == Domain.Notifications.NotificationType.CommentAdded).Subject;
        reply.Title.Should().Contain(ticket.Number).And.NotContain("Sofía", "las notificaciones no llevan datos personales");

        (await customer.PostAsync($"/api/v1/notifications/{reply.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await agent.PostAsync($"/api/v1/notifications/{reply.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound, "nadie marca notificaciones ajenas");
        (await customer.PostAsync("/api/v1/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await customer.GetAsync("/api/v1/notifications/unread-count")).ReadAsync<UnreadCountDto>()).Count.Should().Be(0);
        (await (await customer.GetAsync("/api/v1/notifications")).ReadAsync<List<NotificationDto>>()).Should().Contain(n => n.Id == reply.Id && n.IsRead);
    }
}
