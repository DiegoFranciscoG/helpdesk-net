using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Helpdesk.Application.Suggestions;
using Helpdesk.Contracts.Auth;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Tickets;
using Helpdesk.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Helpdesk.IntegrationTests;

/// <summary>
/// Integración con nlp-tickets-es (P27) simulado con WireMock según su contrato real (snake_case,
/// X-API-Key, /api/v1/classify y /api/v1/feedback).
/// </summary>
public sealed class SuggestionTests(HelpdeskFixture fixture)
{
    [Fact]
    public async Task Classifier_suggestion_is_stored_for_review_and_accepting_it_reclassifies_and_transfers()
    {
        var marker = Marker();
        StubClassify(marker, category: "security_fraud", team: "fraud_security", urgency: "high");
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var ticket = await customer.CreateTicketAsync(title: $"Compras no reconocidas {marker}", impact: Impact.Medium, urgency: Urgency.Low);

        (await ProcessAsync(ticket.Id)).Should().BeTrue();

        var suggestions = await (await agent.GetAsync($"/api/v1/tickets/{ticket.Id}/suggestions")).ReadAsync<List<SuggestionDto>>();
        var suggestion = suggestions.Should().ContainSingle().Subject;
        suggestion.Status.Should().Be(SuggestionStatus.Pending);
        suggestion.CategoryName.Should().Be("Seguridad y fraude");
        suggestion.TeamName.Should().Be("Fraude y Seguridad");
        suggestion.Urgency.Should().Be(Urgency.High);
        suggestion.ResultingPriority.Should().Be(Priority.High);
        suggestion.TeamConfidence.Should().Be(0.953m);
        suggestion.ModelVersion.Should().Be("2026.09.25-7a9c6964");

        var request = fixture.Nlp.LogEntries.Last(e => e.RequestMessage?.Body?.Contains(marker, StringComparison.Ordinal) == true).RequestMessage!;
        request.Headers!["X-API-Key"].Should().ContainSingle(fixture.NlpClientKey);
        request.Body!.Should().Contain("\"external_ref\":\"" + ticket.Number + "\"").And.Contain("\"channel\":\"web\"").And.Contain("\"persist\":true");

        var accepted = await (await agent.PostAsync($"/api/v1/suggestions/{suggestion.Id}/accept", null)).ReadAsync<SuggestionDto>();
        accepted.Status.Should().Be(SuggestionStatus.Accepted);

        var supervisor = await fixture.ClientAsAsync("supervisor");
        var updated = await supervisor.GetTicketAsync(ticket.Id);
        updated.CategoryName.Should().Be("Seguridad y fraude");
        updated.TeamName.Should().Be("Fraude y Seguridad");
        updated.Urgency.Should().Be(Urgency.High);
        updated.Priority.Should().Be(Priority.High);
        updated.Status.Should().Be(TicketStatus.New);
    }

    [Fact]
    public async Task Correcting_a_suggestion_applies_the_agent_values_and_sends_feedback_to_the_classifier()
    {
        var marker = Marker();
        StubClassify(marker, category: "cards", team: "card_operations", urgency: "low");
        fixture.Nlp.Given(Request.Create().WithPath("/api/v1/feedback").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new { id = Guid.NewGuid(), status = "accepted" }));
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var ticket = await customer.CreateTicketAsync(title: $"Transferencia no acreditada {marker}");
        await ProcessAsync(ticket.Id);
        var suggestion = (await (await agent.GetAsync($"/api/v1/tickets/{ticket.Id}/suggestions")).ReadAsync<List<SuggestionDto>>()).Single();
        var categories = await (await agent.GetAsync("/api/v1/categories")).ReadAsync<List<Contracts.Catalog.CategoryDto>>();
        var transfers = categories.Single(c => c.Code == "transfers");

        var corrected = await (await agent.PostJsonAsync($"/api/v1/suggestions/{suggestion.Id}/correct", new CorrectSuggestionRequest { CategoryId = transfers.Id, TeamId = transfers.DefaultTeamId, Urgency = Urgency.Medium }))
            .ReadAsync<SuggestionDto>();

        corrected.Status.Should().Be(SuggestionStatus.Corrected);
        var feedback = fixture.Nlp.LogEntries.Last(e => e.RequestMessage?.Path == "/api/v1/feedback").RequestMessage!;
        feedback.Headers!["X-API-Key"].Should().ContainSingle(fixture.NlpReviewerKey);
        feedback.Body!.Should().Contain("\"corrected_category\":\"transfers\"").And.Contain("\"corrected_team\":\"payments_transfers\"").And.Contain("\"corrected_urgency\":\"medium\"");
    }

    [Fact]
    public async Task Classifier_outage_leaves_the_ticket_untouched()
    {
        var marker = Marker();
        fixture.Nlp.Given(Request.Create().WithPath("/api/v1/classify").UsingPost().WithBody(new WildcardMatcher($"*{marker}*")))
            .RespondWith(Response.Create().WithStatusCode(503));
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var ticket = await customer.CreateTicketAsync(title: $"Caída del clasificador {marker}");

        (await ProcessAsync(ticket.Id)).Should().BeFalse();

        (await (await agent.GetAsync($"/api/v1/tickets/{ticket.Id}/suggestions")).ReadAsync<List<SuggestionDto>>()).Should().BeEmpty();
        (await agent.GetTicketAsync(ticket.Id)).Status.Should().Be(TicketStatus.New);
    }

    [Fact]
    public async Task Untrusted_classifier_output_is_sanitized()
    {
        var marker = Marker();
        StubClassify(marker, category: "'; DROP TABLE tickets;--", team: "<script>", urgency: "extreme", score: 7.5);
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync(title: $"Respuesta maliciosa {marker}");

        (await ProcessAsync(ticket.Id)).Should().BeFalse("sin códigos válidos no hay nada que sugerir");
    }

    [Fact]
    public async Task Integration_client_pushes_a_suggestion_and_duplicates_are_rejected()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync(title: "Recarga no aplicada " + Marker());
        var integration = await IntegrationClientAsync();
        var push = new PushSuggestionRequest { Provider = "nlp-tickets-es", ModelVersion = "push-1", CategoryCode = "top_ups", TeamCode = "payments_transfers", Urgency = Urgency.Low, TeamConfidence = 0.9m, Decision = "auto_route" };

        var unclassified = await (await integration.GetAsync("/api/v1/integration/tickets/unclassified?limit=50")).ReadAsync<List<UnclassifiedTicketDto>>();
        var first = await integration.PostJsonAsync($"/api/v1/integration/tickets/{ticket.Id}/suggestions", push);
        var second = await integration.PostJsonAsync($"/api/v1/integration/tickets/{ticket.Id}/suggestions", push);
        var invalid = await integration.PostJsonAsync($"/api/v1/integration/tickets/{ticket.Id}/suggestions", new PushSuggestionRequest { Provider = "x", ModelVersion = "1", TeamConfidence = 2m });

        unclassified.Should().Contain(t => t.Id == ticket.Id);
        (await first.ReadAsync<SuggestionDto>(HttpStatusCode.Created)).Source.Should().Be(SuggestionSource.Integration);
        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await second.ProblemCodeAsync()).Should().Be("suggestion_pending");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Customers_cannot_see_or_review_suggestions()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync();

        (await customer.GetAsync($"/api/v1/tickets/{ticket.Id}/suggestions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.PostAsync($"/api/v1/suggestions/{Guid.NewGuid()}/accept", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Agents_can_request_a_new_suggestion()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var ticket = await customer.CreateTicketAsync();

        (await agent.PostAsync($"/api/v1/tickets/{ticket.Id}/suggestions/request", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    private async Task<bool> ProcessAsync(Guid ticketId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SuggestionService>().ProcessAsync(ticketId, CancellationToken.None);
    }

    private async Task<HttpClient> IntegrationClientAsync()
    {
        var anonymous = fixture.Factory.CreateClient();
        var token = await (await anonymous.PostAsJsonAsync("/api/v1/auth/token", new ClientTokenRequest { ClientId = HelpdeskFixture.IntegrationClientId, ClientSecret = fixture.IntegrationSecret }))
            .ReadAsync<ClientTokenResponse>();
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private void StubClassify(string marker, string category, string team, string urgency, double score = 0.953)
    {
        var body = new
        {
            ticket_id = Guid.NewGuid(),
            prediction_id = Guid.NewGuid(),
            model = new { name = "minilm-multitask", version = "2026.09.25-7a9c6964" },
            decision = "auto_route",
            review_reasons = Array.Empty<string>(),
            confidence_threshold = 0.748,
            intent = new { code = "card_payment_not_recognised", name = "Pago no reconocido", score = 0.91 },
            category = new { code = category, name = "Categoría", score = 0.921 },
            team = new { code = team, name = "Equipo", score },
            urgency = new { code = urgency, score = 0.88, model_code = urgency, escalated_by_rule = false, rules = Array.Empty<string>() },
            sentiment = new { code = "negative", score = 0.7 },
            suggested_resolution = "Bloquear la tarjeta",
            anonymized_text = "texto",
            explanation = new { top_terms = Array.Empty<object>(), rules_triggered = Array.Empty<string>(), anonymized_entities = new { } },
            latency_ms = 6,
        };
        fixture.Nlp
            .Given(Request.Create().WithPath("/api/v1/classify").UsingPost().WithHeader("X-API-Key", fixture.NlpClientKey).WithBody(new WildcardMatcher($"*{marker}*")))
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(body));
    }

    private static string Marker() => "m" + Guid.NewGuid().ToString("N")[..10];
}
