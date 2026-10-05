using Helpdesk.Contracts.Auth;
using Helpdesk.Contracts.Catalog;
using Helpdesk.Contracts.Reports;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Contracts.Validation;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Identity;
using Helpdesk.Infrastructure.Nlp;
using Microsoft.AspNetCore.Identity;

namespace Helpdesk.UnitTests.Application;

public sealed class ContractValidatorTests
{
    [Fact]
    public void Ticket_requests_need_title_and_meaningful_description()
    {
        var validator = new CreateTicketRequestValidator();

        validator.Validate(new CreateTicketRequest { Title = "Error en la app", Description = "La app se cierra al iniciar." }).IsValid.Should().BeTrue();
        var result = validator.Validate(new CreateTicketRequest { Title = "", Description = "corta" });
        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Title", "Description"]);
    }

    [Theory]
    [InlineData(TransitionAction.Hold, null, null, false)]
    [InlineData(TransitionAction.Hold, "Esperando captura", null, true)]
    [InlineData(TransitionAction.Cancel, null, null, false)]
    [InlineData(TransitionAction.Reopen, "Sigue fallando", null, true)]
    [InlineData(TransitionAction.Resolve, null, null, false)]
    [InlineData(TransitionAction.Resolve, null, "Se corrigió", true)]
    [InlineData(TransitionAction.Start, null, null, true)]
    public void Transitions_require_reason_or_summary_when_appropriate(TransitionAction action, string? reason, string? summary, bool valid) =>
        new TransitionRequestValidator().Validate(new TransitionRequest { Action = action, Reason = reason, ResolutionSummary = summary }).IsValid.Should().Be(valid);

    [Fact]
    public void User_creation_follows_nist_length_and_role_rules()
    {
        var validator = new CreateUserRequestValidator();

        validator.Validate(new CreateUserRequest { FullName = "Ana", Email = "ana@helpdesk.example", Password = "una frase larga y fácil", Role = "Agent" }).IsValid.Should().BeTrue();
        validator.Validate(new CreateUserRequest { FullName = "Ana", Email = "ana@helpdesk.example", Password = "Corta1!", Role = "Agent" }).IsValid.Should().BeFalse();
        validator.Validate(new CreateUserRequest { FullName = "Ana", Email = "ana@helpdesk.example", Password = "una frase larga y fácil", Role = "Customer" }).IsValid.Should().BeFalse("un cliente necesita CustomerId");
        validator.Validate(new CreateUserRequest { FullName = "Ana", Email = "ana@helpdesk.example", Password = "una frase larga y fácil", Role = "Root" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Suggestion_requests_are_validated()
    {
        new CorrectSuggestionRequestValidator().Validate(new CorrectSuggestionRequest()).IsValid.Should().BeFalse();
        new CorrectSuggestionRequestValidator().Validate(new CorrectSuggestionRequest { Urgency = Urgency.High }).IsValid.Should().BeTrue();

        var push = new PushSuggestionRequestValidator();
        push.Validate(new PushSuggestionRequest { Provider = "p", ModelVersion = "1", CategoryCode = "cards", TeamConfidence = 0.9m, Decision = "auto_route" }).IsValid.Should().BeTrue();
        push.Validate(new PushSuggestionRequest { Provider = "p", ModelVersion = "1", CategoryCode = "DROP TABLE" }).IsValid.Should().BeFalse();
        push.Validate(new PushSuggestionRequest { Provider = "p", ModelVersion = "1" }).IsValid.Should().BeFalse("sin valores no hay sugerencia");
    }

    [Fact]
    public void Report_ranges_are_limited()
    {
        var validator = new ReportQueryValidator();

        validator.Validate(new ReportQuery { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 6, 30) }).IsValid.Should().BeTrue();
        validator.Validate(new ReportQuery { From = new DateOnly(2026, 6, 1), To = new DateOnly(2026, 1, 1) }).IsValid.Should().BeFalse();
        validator.Validate(new ReportQuery { From = new DateOnly(2024, 1, 1), To = new DateOnly(2026, 1, 1) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Other_requests_are_validated()
    {
        new LoginRequestValidator().Validate(new LoginRequest { Email = "no-es-correo", Password = "x" }).IsValid.Should().BeFalse();
        new PrivacyConsentRequestValidator().Validate(new PrivacyConsentRequest { Version = "2020-01" }).IsValid.Should().BeFalse();
        new SaveCustomerRequestValidator().Validate(new SaveCustomerRequest { DisplayName = "Ana", Phone = "abc" }).IsValid.Should().BeFalse();
        new UpdateSlaPolicyRequestValidator().Validate(new UpdateSlaPolicyRequest { FirstResponseMinutes = 60, ResolutionMinutes = 30 }).IsValid.Should().BeFalse();
        new CreateHolidayRequestValidator().Validate(new CreateHolidayRequest { Date = new DateOnly(2027, 1, 1), Name = "Año Nuevo", LegalBasis = "" }).IsValid.Should().BeFalse();
        new TicketQueryValidator().Validate(new TicketQuery { PageSize = 101 }).IsValid.Should().BeFalse();
        new SetTeamMembersRequestValidator().Validate(new SetTeamMembersRequest { Members = [new() { UserId = Guid.Empty }] }).IsValid.Should().BeFalse();
        AttachmentClientRules.IsAllowedExtension("foto.JPG").Should().BeTrue();
        AttachmentClientRules.IsAllowedExtension("macro.docm").Should().BeFalse();
    }
}

public sealed class PasswordPolicyTests
{
    private readonly CommonPasswordValidator _validator = new();
    private readonly AppUser _user = new() { Email = "carlos.velez@helpdesk.example", FullName = "Carlos Vélez" };

    [Theory]
    [InlineData("rio tomebamba al atardecer")]
    [InlineData("Cuatro-Ríos-Y-Un-Puente-2026")]
    public async Task Long_uncommon_passphrases_are_accepted(string password) =>
        (await _validator.ValidateAsync(null!, _user, password)).Succeeded.Should().BeTrue();

    [Theory]
    [InlineData("123456789012345")]
    [InlineData("contraseña123456")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("98765432109876543")]
    [InlineData("abcabcabcabcabcabc")]
    [InlineData("carlos.velez-2026-segura")]
    [InlineData("mi clave de Carlos es larga")]
    public async Task Common_predictable_or_personal_passwords_are_rejected(string password) =>
        (await _validator.ValidateAsync(null!, _user, password)).Succeeded.Should().BeFalse();

    [Fact]
    public async Task Passwords_longer_than_the_maximum_are_rejected() =>
        (await _validator.ValidateAsync(null!, _user, string.Concat(Enumerable.Repeat("larga frase ", 20)))).Errors.Should().Contain(e => e.Code == "PasswordTooLong");

    [Fact]
    public void Spanish_messages_are_used()
    {
        var describer = new SpanishIdentityErrorDescriber();

        describer.DuplicateEmail("x").Description.Should().Contain("correo");
        describer.PasswordTooShort(15).Description.Should().Contain("15");
        describer.InvalidEmail("x").Code.Should().Be(nameof(IdentityErrorDescriber.InvalidEmail));
        describer.DuplicateUserName("x").Description.Should().NotBeEmpty();
        describer.PasswordRequiresUniqueChars(3).Description.Should().Contain("3");
        describer.InvalidRoleName("x").Description.Should().NotBeEmpty();
    }
}

public sealed class NlpMappingTests
{
    [Fact]
    public void Response_is_mapped_with_validated_codes_and_scores()
    {
        var response = new NlpTicketsClient.ClassifyResponse(
            Guid.NewGuid(), Guid.NewGuid(), new NlpTicketsClient.ModelRef("minilm", "2026.09.25-7a9c6964"), "human_review", ["low_confidence", "BAD CODE"],
            new NlpTicketsClient.LabelScore("security_fraud", "Seguridad", 0.91234), new NlpTicketsClient.LabelScore("fraud_security", "Fraude", 1.7), new NlpTicketsClient.LabelScore("high", null, 0.8));

        var mapped = NlpTicketsClient.Map(response);

        mapped.ModelVersion.Should().Be("2026.09.25-7a9c6964");
        mapped.Suggestion.CategoryCode.Should().Be("security_fraud");
        mapped.Suggestion.CategoryConfidence.Should().Be(0.912m);
        mapped.Suggestion.TeamConfidence.Should().BeNull("un puntaje fuera de 0–1 se descarta");
        mapped.Suggestion.Urgency.Should().Be(Urgency.High);
        mapped.Suggestion.Decision.Should().Be("human_review");
        mapped.Suggestion.ReviewReasons.Should().Equal("low_confidence");
    }

    [Theory]
    [InlineData(TicketChannel.Portal, "web")]
    [InlineData(TicketChannel.Email, "email")]
    [InlineData(TicketChannel.Phone, "phone")]
    [InlineData(TicketChannel.Chat, "chat")]
    [InlineData(TicketChannel.WalkIn, "branch")]
    public void Channels_map_to_the_classifier_vocabulary(TicketChannel channel, string expected) =>
        NlpTicketsClient.MapChannel(channel).Should().Be(expected);

    [Theory]
    [InlineData("low", Urgency.Low)]
    [InlineData("medium", Urgency.Medium)]
    [InlineData("high", Urgency.High)]
    [InlineData("urgent", null)]
    [InlineData(null, null)]
    public void Urgency_codes_are_parsed(string? code, Urgency? expected) =>
        NlpTicketsClient.ParseUrgency(code).Should().Be(expected);

    [Fact]
    public void Unknown_model_version_is_labelled()
    {
        var response = new NlpTicketsClient.ClassifyResponse(null, null, null, "auto_route", null, null, null, null);

        NlpTicketsClient.Map(response).ModelVersion.Should().Be("desconocida");
    }
}

public sealed class TokenServiceTests
{
    [Fact]
    public void Refresh_tokens_are_random_and_only_their_hash_is_stored()
    {
        var a = TokenService.NewRefreshToken();
        var b = TokenService.NewRefreshToken();

        a.Should().NotBe(b);
        a.Length.Should().BeGreaterThanOrEqualTo(43, "32 bytes en base64url");
        TokenService.Hash(a).Should().HaveLength(64).And.NotBe(a);
        TokenService.Hash(a).Should().Be(TokenService.Hash(a));
    }

    [Fact]
    public void Signing_keys_shorter_than_256_bits_are_rejected()
    {
        var validator = new JwtOptionsValidator();

        validator.Validate(null, new JwtOptions { SigningKey = "corta" }).Failed.Should().BeTrue();
        validator.Validate(null, new JwtOptions { SigningKey = new string('k', 32) }).Succeeded.Should().BeTrue();
    }
}
