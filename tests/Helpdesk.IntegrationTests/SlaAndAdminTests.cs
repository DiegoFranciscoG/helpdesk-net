using System.Net;
using System.Net.Http.Headers;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Catalog;
using Helpdesk.Contracts.Catalog;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Persistence.Seed;
using Helpdesk.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Helpdesk.IntegrationTests;

/// <summary>Pruebas que cambian datos globales (reloj del monitor, feriados, reglas): corren sin paralelismo.</summary>
[CollectionDefinition(nameof(GlobalStateGroup), DisableParallelization = true)]
public sealed class GlobalStateGroup;

[Collection(nameof(GlobalStateGroup))]
public sealed class SlaMonitorTests(HelpdeskFixture fixture)
{
    [Fact]
    public async Task Monitor_raises_warning_then_breach_and_escalates_to_supervisors()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync(title: "Tarjeta clonada, consumos en el exterior", impact: Impact.High, urgency: Urgency.High);
        ticket.Sla.PolicyName.Should().Be("P1 Crítica");

        await RunMonitorAtAsync(ticket.CreatedAt.AddMinutes(24));
        var warned = await LoadAsync(ticket.Id);
        warned.ResponseWarningSent.Should().BeTrue();
        warned.FirstResponseBreached.Should().BeFalse();
        warned.EscalationLevel.Should().Be(1);

        await RunMonitorAtAsync(ticket.CreatedAt.AddMinutes(31));
        var breached = await LoadAsync(ticket.Id);
        breached.FirstResponseBreached.Should().BeTrue();
        breached.EscalationLevel.Should().Be(2);

        var supervisor = await fixture.ClientAsAsync("supervisor");
        var notifications = await (await supervisor.GetAsync("/api/v1/notifications")).ReadAsync<List<NotificationDto>>();
        notifications.Should().Contain(n => n.TicketId == ticket.Id && n.Type == NotificationType.SlaBreached);
        var lead = await fixture.ClientAsAsync("agente");
        (await (await lead.GetAsync("/api/v1/notifications")).ReadAsync<List<NotificationDto>>())
            .Should().Contain(n => n.TicketId == ticket.Id && n.Type == NotificationType.SlaWarning, "sin asignado, avisa al líder de la cola");

        var list = await (await supervisor.GetAsync("/api/v1/tickets?sla=breached&pageSize=100&search=" + Uri.EscapeDataString(ticket.Number))).ReadAsync<Contracts.Common.PagedResult<TicketListItemDto>>();
        list.Items.Should().ContainSingle(t => t.Id == ticket.Id && t.SlaState == SlaState.Breached);
    }

    [Fact]
    public async Task Monitor_auto_closes_resolved_tickets_after_three_business_days()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var agentId = (await fixture.TokenAsync("agente@helpdesk.example")).User.Id;
        var ticket = await customer.CreateTicketAsync();
        await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new AssignRequest { AssigneeId = agentId });
        await agent.TransitionAsync(ticket.Id, TransitionAction.Start);
        var resolved = await agent.TransitionAsync(ticket.Id, TransitionAction.Resolve, summary: "Se reinició la clave de acceso.");

        await RunMonitorAtAsync(resolved.AutoCloseAt!.Value.AddMinutes(1));

        var closed = await customer.GetTicketAsync(ticket.Id);
        closed.Status.Should().Be(TicketStatus.Closed);
        closed.History[^1].ChangedByName.Should().Be("Sistema");
        closed.AllowedActions.Should().Contain(TicketAction.Survey, "el cliente aún puede calificar un ticket cerrado");
    }

    [Fact]
    public async Task Paused_tickets_do_not_breach_resolution()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var agentId = (await fixture.TokenAsync("agente@helpdesk.example")).User.Id;
        var ticket = await customer.CreateTicketAsync(impact: Impact.High, urgency: Urgency.High);
        await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new AssignRequest { AssigneeId = agentId });
        await agent.TransitionAsync(ticket.Id, TransitionAction.Start);
        await agent.TransitionAsync(ticket.Id, TransitionAction.Hold, reason: "Esperando al proveedor");

        await RunMonitorAtAsync(ticket.Sla.ResolutionDueAt.AddHours(5));

        (await LoadAsync(ticket.Id)).ResolutionBreached.Should().BeFalse();
    }

    private async Task RunMonitorAtAsync(DateTimeOffset instant)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var monitor = new SlaMonitorService(
            scope.ServiceProvider.GetRequiredService<ITicketRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            new FakeTimeProvider(instant));
        await monitor.RunOnceAsync(CancellationToken.None);
    }

    private async Task<Ticket> LoadAsync(Guid id)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>().Tickets.AsNoTracking().SingleAsync(t => t.Id == id);
    }
}

[Collection(nameof(GlobalStateGroup))]
public sealed class AdministrationTests(HelpdeskFixture fixture)
{
    private static readonly TimeZoneInfo Guayaquil = TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil");

    [Fact]
    public async Task A_new_holiday_moves_the_due_date_of_new_tickets()
    {
        var admin = await fixture.ClientAsAsync("admin");
        var customer = await fixture.CustomerWithConsentAsync();
        var before = await customer.CreateTicketAsync(title: "Antes del feriado", impact: Impact.Medium, urgency: Urgency.Medium);

        var holidayDate = NextWorkingDayWithoutHoliday();
        var holiday = await (await admin.PostJsonAsync($"/api/v1/calendars/{ReferenceData.OfficeCalendarId}/holidays", new CreateHolidayRequest
        {
            Date = holidayDate,
            Name = "Feriado local de prueba",
            LegalBasis = "Ordenanza municipal (prueba)",
        })).ReadAsync<HolidayDto>(HttpStatusCode.Created);

        try
        {
            var after = await customer.CreateTicketAsync(title: "Después del feriado", impact: Impact.Medium, urgency: Urgency.Medium);
            (after.Sla.ResolutionDueAt - before.Sla.ResolutionDueAt).Should().BeGreaterThanOrEqualTo(TimeSpan.FromHours(23), "el día feriado no cuenta como tiempo hábil");

            var duplicate = await admin.PostJsonAsync($"/api/v1/calendars/{ReferenceData.OfficeCalendarId}/holidays", new CreateHolidayRequest { Date = holidayDate, Name = "Repetido", LegalBasis = "x" });
            duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await duplicate.ProblemCodeAsync()).Should().Be("holiday_exists");
        }
        finally
        {
            (await admin.DeleteAsync($"/api/v1/calendars/{ReferenceData.OfficeCalendarId}/holidays/{holiday.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Calendars_expose_the_official_2026_holidays()
    {
        var supervisor = await fixture.ClientAsAsync("supervisor");

        var calendars = await (await supervisor.GetAsync("/api/v1/calendars")).ReadAsync<List<CalendarDto>>();

        var office = calendars.Single(c => c.Code == ReferenceData.OfficeCalendarCode);
        office.TimeZone.Should().Be("America/Guayaquil");
        office.Hours.Should().HaveCount(10);
        office.Holidays.Should().Contain(h => h.Date == new DateOnly(2026, 5, 25) && h.LegalBasis.Contains("906", StringComparison.Ordinal));
        calendars.Single(c => c.Code == "24x7").Is24x7.Should().BeTrue();
    }

    [Fact]
    public async Task Sla_policies_are_validated_and_only_admins_change_them()
    {
        var admin = await fixture.ClientAsAsync("admin");
        var supervisor = await fixture.ClientAsAsync("supervisor");
        var policies = await (await supervisor.GetAsync("/api/v1/sla-policies")).ReadAsync<List<SlaPolicyDto>>();
        var p4 = policies.Single(p => p.Priority == Priority.Low && p.ServiceTier == null);

        var bySupervisor = await supervisor.PutJsonAsync($"/api/v1/sla-policies/{p4.Id}", new UpdateSlaPolicyRequest { FirstResponseMinutes = 400, ResolutionMinutes = 2000, WarningPercent = 80 });
        var invalid = await admin.PutJsonAsync($"/api/v1/sla-policies/{p4.Id}", new UpdateSlaPolicyRequest { FirstResponseMinutes = 500, ResolutionMinutes = 100, WarningPercent = 80 });
        var valid = await admin.PutJsonAsync($"/api/v1/sla-policies/{p4.Id}", new UpdateSlaPolicyRequest { FirstResponseMinutes = 480, ResolutionMinutes = 2400, WarningPercent = 80 });

        policies.Should().HaveCount(8);
        bySupervisor.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await valid.ReadAsync<SlaPolicyDto>()).WarningPercent.Should().Be(80);
        await admin.PutJsonAsync($"/api/v1/sla-policies/{p4.Id}", new UpdateSlaPolicyRequest { FirstResponseMinutes = 480, ResolutionMinutes = 2400, WarningPercent = 75 });
    }

    [Fact]
    public async Task Anonymizing_a_customer_erases_contact_data_users_and_their_files()
    {
        var admin = await fixture.ClientAsAsync("admin");
        var customer = await (await admin.PostJsonAsync("/api/v1/customers", new SaveCustomerRequest { DisplayName = "María Ficticia", Email = "maria.ficticia@correo.example", Phone = "0990000000" }))
            .ReadAsync<CustomerDto>(HttpStatusCode.Created);
        var email = $"maria-{Guid.NewGuid():N}@correo.example";
        var password = "Clave-Segura-" + Guid.NewGuid().ToString("N")[..10];
        await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest { FullName = "María Ficticia", Email = email, Password = password, Role = "Customer", CustomerId = customer.Id });
        var portal = fixture.Factory.CreateClient();
        portal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await HelpdeskFixture.LoginAsync(fixture.Factory.CreateClient(), email, password)).AccessToken);
        await portal.PostJsonAsync("/api/v1/auth/privacy-consent", new Contracts.Auth.PrivacyConsentRequest { Version = Contracts.Common.PrivacyNotice.CurrentVersion });
        portal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await HelpdeskFixture.LoginAsync(fixture.Factory.CreateClient(), email, password)).AccessToken);
        var ticket = await portal.CreateTicketAsync(title: "Actualizar mis datos");
        var form = new MultipartFormDataContent { { new ByteArrayContent("Mi dirección nueva es ficticia"u8.ToArray()), "file", "datos.txt" }, { new StringContent("false"), "isInternal" } };
        var attachment = await (await portal.PostAsync($"/api/v1/tickets/{ticket.Id}/attachments", form)).ReadAsync<AttachmentDto>(HttpStatusCode.Created);

        var anonymized = await (await admin.PostAsync($"/api/v1/customers/{customer.Id}/anonymize", null)).ReadAsync<CustomerDto>();

        anonymized.DisplayName.Should().StartWith("Cliente anonimizado");
        anonymized.Email.Should().BeNull();
        anonymized.Phone.Should().BeNull();
        anonymized.AnonymizedAt.Should().NotBeNull();
        var login = await fixture.Factory.CreateClient().PostJsonAsync("/api/v1/auth/login", new Contracts.Auth.LoginRequest { Email = email, Password = password });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var supervisor = await fixture.ClientAsAsync("supervisor");
        (await supervisor.GetAsync($"/api/v1/attachments/{attachment.Id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var kept = await supervisor.GetTicketAsync(ticket.Id);
        kept.CustomerName.Should().StartWith("Cliente anonimizado", "el ticket se conserva para estadística sin datos personales");
        (await admin.PutJsonAsync($"/api/v1/customers/{customer.Id}", new SaveCustomerRequest { DisplayName = "Otra" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Admin_manages_team_members_and_users()
    {
        var admin = await fixture.ClientAsAsync("admin");
        var users = await (await admin.GetAsync("/api/v1/users")).ReadAsync<List<UserDto>>();
        var diana = users.Single(u => u.Email == "diana.torres@helpdesk.example");
        var teams = await (await admin.GetAsync("/api/v1/teams")).ReadAsync<List<TeamDto>>();
        var care = teams.Single(t => t.Code == "customer_care");
        var members = care.Members.Select(m => new TeamMemberRequest { UserId = m.UserId, IsLead = m.IsLead }).ToList();
        members.Add(new TeamMemberRequest { UserId = diana.Id, IsLead = false });

        var updated = await (await admin.PutJsonAsync($"/api/v1/teams/{care.Id}/members", new SetTeamMembersRequest { Members = members })).ReadAsync<TeamDto>();
        var customerAsMember = await admin.PutJsonAsync($"/api/v1/teams/{care.Id}/members", new SetTeamMembersRequest { Members = [new TeamMemberRequest { UserId = users.First(u => u.Role == "Customer").Id }] });
        var reverted = await (await admin.PutJsonAsync($"/api/v1/teams/{care.Id}/members", new SetTeamMembersRequest { Members = care.Members.Select(m => new TeamMemberRequest { UserId = m.UserId, IsLead = m.IsLead }).ToList() })).ReadAsync<TeamDto>();

        updated.Members.Should().Contain(m => m.UserId == diana.Id);
        customerAsMember.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        reverted.Members.Should().NotContain(m => m.UserId == diana.Id);
        CatalogService.PriorityMatrixCells().Should().HaveCount(9);
    }

    [Fact]
    public async Task Deactivated_users_lose_access()
    {
        var admin = await fixture.ClientAsAsync("admin");
        var email = $"temporal-{Guid.NewGuid():N}@helpdesk.example";
        var password = "Clave-Segura-" + Guid.NewGuid().ToString("N")[..10];
        var created = await (await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest { FullName = "Agente Temporal", Email = email, Password = password, Role = "Agent", TeamIds = [ReferenceData.CustomerCareTeamId] })).ReadAsync<UserDto>(HttpStatusCode.Created);
        var session = await HelpdeskFixture.LoginAsync(fixture.Factory.CreateClient(), email, password);

        (await admin.PutJsonAsync($"/api/v1/users/{created.Id}/status", new SetUserStatusRequest { IsActive = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.Factory.CreateClient().PostJsonAsync("/api/v1/auth/login", new Contracts.Auth.LoginRequest { Email = email, Password = password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.Factory.CreateClient().PostJsonAsync("/api/v1/auth/refresh", new Contracts.Auth.RefreshRequest { RefreshToken = session.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var selfId = (await fixture.TokenAsync("admin@helpdesk.example")).User.Id;
        (await admin.PutJsonAsync($"/api/v1/users/{selfId}/status", new SetUserStatusRequest { IsActive = false })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static DateOnly NextWorkingDayWithoutHoliday()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Guayaquil).DateTime);
        var official = ReferenceData.Holidays2026.Select(h => h.Date).ToHashSet();
        var day = today.AddDays(1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || official.Contains(day))
        {
            day = day.AddDays(1);
        }

        return day;
    }
}
