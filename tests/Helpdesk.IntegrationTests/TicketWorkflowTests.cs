using System.Net;
using Helpdesk.Contracts.Auth;
using Helpdesk.Contracts.Common;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Persistence.Seed;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

/// <summary>Ciclo de vida completo a través de la API real, con PostgreSQL y la máquina de estados.</summary>
public sealed class TicketWorkflowTests(HelpdeskFixture fixture)
{
    [Fact]
    public async Task Customer_must_accept_the_privacy_notice_before_opening_a_ticket()
    {
        var (client, _) = await NewPortalUserAsync();

        var before = await client.PostJsonAsync("/api/v1/tickets", new CreateTicketRequest { Title = "Consulta", Description = "Necesito ayuda con mi cuenta." });
        var consent = await client.PostJsonAsync("/api/v1/auth/privacy-consent", new PrivacyConsentRequest { Version = PrivacyNotice.CurrentVersion });

        before.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await before.ProblemCodeAsync()).Should().Be("privacy_notice_required");
        (await consent.ReadAsync<CurrentUserDto>()).PrivacyNoticeAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task Full_lifecycle_from_new_to_closed_with_pause_and_survey()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var agentId = (await fixture.TokenAsync("agente@helpdesk.example")).User.Id;

        var ticket = await customer.CreateTicketAsync(impact: Impact.High, urgency: Urgency.Medium);
        ticket.Status.Should().Be(TicketStatus.New);
        ticket.Priority.Should().Be(Priority.High);
        ticket.Channel.Should().Be(TicketChannel.Portal);
        ticket.TeamId.Should().Be(ReferenceData.ServiceDeskTeamId);
        ticket.Sla.PolicyName.Should().Be("P2 Alta");
        ticket.AllowedActions.Should().Contain(TicketAction.Cancel).And.NotContain(TicketAction.Assign);

        var assigned = await (await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new AssignRequest { AssigneeId = agentId })).ReadAsync<TicketDetailDto>();
        assigned.Status.Should().Be(TicketStatus.Assigned);
        assigned.AssigneeName.Should().Be("Carlos Vélez");

        var started = await agent.TransitionAsync(ticket.Id, TransitionAction.Start);
        started.Status.Should().Be(TicketStatus.InProgress);
        started.Sla.FirstRespondedAt.Should().NotBeNull();

        var onHold = await agent.TransitionAsync(ticket.Id, TransitionAction.Hold, reason: "Esperando captura del error");
        onHold.Sla.State.Should().Be(SlaState.Paused);

        await customer.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new CreateCommentRequest { Body = "Adjunto la captura solicitada." });
        (await agent.GetTicketAsync(ticket.Id)).Status.Should().Be(TicketStatus.InProgress, "la respuesta del cliente reanuda el ticket");

        var resolved = await agent.TransitionAsync(ticket.Id, TransitionAction.Resolve, summary: "Se restableció el acceso a la banca móvil.");
        resolved.Status.Should().Be(TicketStatus.Resolved);
        resolved.AutoCloseAt.Should().NotBeNull();

        var closed = await (await customer.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/survey", new SurveyRequest { Rating = 5, Comment = "Excelente" })).ReadAsync<TicketDetailDto>();
        closed.Status.Should().Be(TicketStatus.Closed);
        closed.Survey!.Rating.Should().Be(5);
        closed.History.Select(h => h.ToStatus).Should().Equal(
            TicketStatus.New, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.OnHold, TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed);
    }

    [Fact]
    public async Task Invalid_transitions_return_conflict_and_agents_cannot_close()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var ticket = await customer.CreateTicketAsync();

        var resolveNew = await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/transitions", new TransitionRequest { Action = TransitionAction.Resolve, ResolutionSummary = "x" });
        resolveNew.StatusCode.Should().Be(HttpStatusCode.Forbidden, "solo el asignado o supervisión trabajan el ticket");

        var supervisor = await fixture.ClientAsAsync("supervisor");
        var resolveBySupervisor = await supervisor.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/transitions", new TransitionRequest { Action = TransitionAction.Resolve, ResolutionSummary = "x" });
        resolveBySupervisor.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resolveBySupervisor.ProblemCodeAsync()).Should().Be("invalid_transition");
    }

    [Fact]
    public async Task Stale_if_match_returns_precondition_failed()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var agentId = (await fixture.TokenAsync("agente@helpdesk.example")).User.Id;
        var ticket = await customer.CreateTicketAsync();

        var stale = await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new AssignRequest { AssigneeId = agentId }, ifMatch: $"\"{ticket.Version + 1000}\"");
        var fresh = await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new AssignRequest { AssigneeId = agentId }, ifMatch: $"\"{ticket.Version}\"");

        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
        fresh.Headers.ETag!.Tag.Should().NotBe($"\"{ticket.Version}\"");
    }

    [Fact]
    public async Task Customers_cannot_see_tickets_of_other_customers()
    {
        var owner = await fixture.CustomerWithConsentAsync();
        var ticket = await owner.CreateTicketAsync();
        var stranger = await fixture.ClientAsAsync("luis.cardenas@correo.example");

        (await stranger.GetAsync($"/api/v1/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await (await stranger.GetAsync("/api/v1/tickets?pageSize=100")).ReadAsync<PagedResult<TicketListItemDto>>();
        list.Items.Should().NotContain(t => t.Id == ticket.Id).And.OnlyContain(t => t.CustomerCode == "CLI-00003");
    }

    [Fact]
    public async Task Agents_outside_the_queue_cannot_see_the_ticket()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync();
        var fraudAgent = await fixture.ClientAsAsync("andres.ortiz");

        (await fraudAgent.GetAsync($"/api/v1/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Internal_comments_are_hidden_from_customers()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var ticket = await customer.CreateTicketAsync();

        (await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new CreateCommentRequest { Body = "Nota interna", IsInternal = true })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new CreateCommentRequest { Body = "Estamos revisando su caso" })).StatusCode.Should().Be(HttpStatusCode.Created);
        var forbidden = await customer.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new CreateCommentRequest { Body = "x", IsInternal = true });

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetTicketAsync(ticket.Id)).Comments.Should().ContainSingle(c => c.Body == "Estamos revisando su caso" && c.AuthorIsStaff);
        (await agent.GetTicketAsync(ticket.Id)).Comments.Should().HaveCount(2);
        (await agent.GetTicketAsync(ticket.Id)).Sla.FirstRespondedAt.Should().NotBeNull("el comentario público del agente es la primera respuesta");
    }

    [Fact]
    public async Task Staff_opens_tickets_for_a_customer_and_reclassification_recalculates_priority()
    {
        var supervisor = await fixture.ClientAsAsync("supervisor");
        var customers = await (await supervisor.GetAsync("/api/v1/customers?search=Nubeverde")).ReadAsync<PagedResult<Contracts.Catalog.CustomerDto>>();
        var premium = customers.Items.Single();

        var ticket = await supervisor.CreateTicketAsync(title: "Nómina rechazada", impact: Impact.Medium, urgency: Urgency.Medium, customerId: premium.Id, channel: TicketChannel.Phone);
        ticket.Channel.Should().Be(TicketChannel.Phone);
        ticket.Sla.PolicyName.Should().Be("P3 Media premium");

        var classified = await (await supervisor.PutJsonAsync($"/api/v1/tickets/{ticket.Id}/classification", new ClassifyRequest { Impact = Impact.High, Urgency = Urgency.High })).ReadAsync<TicketDetailDto>();
        classified.Priority.Should().Be(Priority.Critical);
        classified.Sla.PolicyName.Should().Be("P1 Crítica premium");
        (classified.Sla.ResolutionDueAt - classified.CreatedAt).Should().BeCloseTo(TimeSpan.FromMinutes(120), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Staff_must_name_the_customer_and_validation_errors_are_problem_details()
    {
        var supervisor = await fixture.ClientAsAsync("supervisor");

        var missingCustomer = await supervisor.PostJsonAsync("/api/v1/tickets", new CreateTicketRequest { Title = "Sin cliente", Description = "Descripción suficientemente larga." });
        var invalid = await supervisor.PostJsonAsync("/api/v1/tickets", new CreateTicketRequest { Title = "", Description = "corta" });

        missingCustomer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await missingCustomer.ProblemCodeAsync()).Should().Be("customer_required");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await invalid.Content.ReadAsStringAsync();
        body.Should().Contain("\"title\"").And.Contain("\"description\"").And.Contain("traceId").And.NotContain("   at ");
    }

    [Fact]
    public async Task Transfer_moves_the_ticket_to_another_queue_and_frees_the_assignee()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var agentId = (await fixture.TokenAsync("agente@helpdesk.example")).User.Id;
        var ticket = await customer.CreateTicketAsync();
        await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new AssignRequest { AssigneeId = agentId });

        var transferred = await (await agent.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/transfer", new TransferRequest { TeamId = ReferenceData.FraudSecurityTeamId, Reason = "Posible fraude" })).ReadAsync<TicketDetailDto>();

        transferred.Status.Should().Be(TicketStatus.New);
        transferred.TeamName.Should().Be("Fraude y Seguridad");
        transferred.AssigneeId.Should().BeNull();
        (await agent.GetAsync($"/api/v1/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "el agente ya no pertenece a la cola del ticket");
    }

    [Fact]
    public async Task Assigning_to_someone_outside_the_queue_is_rejected()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var supervisor = await fixture.ClientAsAsync("supervisor");
        var fraudAgentId = (await fixture.TokenAsync("andres.ortiz@helpdesk.example")).User.Id;
        var ticket = await customer.CreateTicketAsync();

        var response = await supervisor.PostJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new AssignRequest { AssigneeId = fraudAgentId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ProblemCodeAsync()).Should().Be("assignee_not_in_team");
    }

    [Fact]
    public async Task Ticket_list_filters_and_search_work()
    {
        var supervisor = await fixture.ClientAsAsync("supervisor");
        var customer = await fixture.CustomerWithConsentAsync();
        var created = await customer.CreateTicketAsync(title: "Búsqueda única zafiro", impact: Impact.High, urgency: Urgency.High);

        var byText = await (await supervisor.GetAsync("/api/v1/tickets?search=zafiro")).ReadAsync<PagedResult<TicketListItemDto>>();
        var byNumber = await (await supervisor.GetAsync($"/api/v1/tickets?search={created.Number}")).ReadAsync<PagedResult<TicketListItemDto>>();
        var critical = await (await supervisor.GetAsync("/api/v1/tickets?priority=critical&openOnly=true&sort=due_date&pageSize=100")).ReadAsync<PagedResult<TicketListItemDto>>();
        var tooBig = await supervisor.GetAsync("/api/v1/tickets?pageSize=500");

        byText.Items.Should().ContainSingle(t => t.Id == created.Id);
        byNumber.Items.Should().ContainSingle(t => t.Id == created.Id);
        critical.Items.Should().OnlyContain(t => t.Priority == Priority.Critical).And.BeInAscendingOrder(t => t.ResolutionDueAt);
        tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Crea un cliente y su usuario del portal desde la administración (sin aviso de privacidad aceptado).</summary>
    internal async Task<(HttpClient Client, Guid CustomerId)> NewPortalUserAsync()
    {
        var admin = await fixture.ClientAsAsync("admin");
        var customer = await (await admin.PostJsonAsync("/api/v1/customers", new Contracts.Catalog.SaveCustomerRequest { DisplayName = "Cliente de prueba " + Guid.NewGuid().ToString("N")[..6] }))
            .ReadAsync<Contracts.Catalog.CustomerDto>(HttpStatusCode.Created);
        var email = $"portal-{Guid.NewGuid():N}@correo.example";
        var password = "Clave-Segura-" + Guid.NewGuid().ToString("N")[..10];
        (await admin.PostJsonAsync("/api/v1/users", new Contracts.Catalog.CreateUserRequest { FullName = "Usuaria Portal", Email = email, Password = password, Role = "Customer", CustomerId = customer.Id }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var token = await HelpdeskFixture.LoginAsync(fixture.Factory.CreateClient(), email, password);
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        return (client, customer.Id);
    }
}
