using FluentValidation;
using Helpdesk.Contracts.Auth;
using Helpdesk.Contracts.Catalog;
using Helpdesk.Contracts.Reports;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Attachments;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Contracts.Validation;

// Validaciones compartidas: el front las ejecuta antes de enviar y la API las vuelve a ejecutar (nunca se confía en el cliente).

/// <summary>Límites de contraseña de NIST SP 800-63B-4: 15 a 128 caracteres, sin reglas de composición.</summary>
public static class PasswordRules
{
    public const int MinLength = 15;
    public const int MaxLength = 128;
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Ingresa tu correo.").MaximumLength(256).EmailAddress().WithMessage("Correo no válido.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Ingresa tu contraseña.").MaximumLength(PasswordRules.MaxLength);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class ClientTokenRequestValidator : AbstractValidator<ClientTokenRequest>
{
    public ClientTokenRequestValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ClientSecret).NotEmpty().MaximumLength(200);
    }
}

public sealed class PrivacyConsentRequestValidator : AbstractValidator<PrivacyConsentRequest>
{
    public PrivacyConsentRequestValidator() =>
        RuleFor(x => x.Version).Equal(Common.PrivacyNotice.CurrentVersion).WithMessage("Debes aceptar la versión vigente del aviso de privacidad.");
}

public sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("El título es obligatorio.")
            .MaximumLength(Ticket.TitleMaxLength).WithMessage($"Máximo {Ticket.TitleMaxLength} caracteres.");
        RuleFor(x => x.Description).NotEmpty().WithMessage("Describe el problema.")
            .MinimumLength(10).WithMessage("Describe el problema con al menos 10 caracteres.")
            .MaximumLength(Ticket.DescriptionMaxLength).WithMessage($"Máximo {Ticket.DescriptionMaxLength} caracteres.");
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Impact).IsInEnum();
        RuleFor(x => x.Urgency).IsInEnum();
    }
}

public sealed class TicketQueryValidator : AbstractValidator<TicketQuery>
{
    public TicketQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority.HasValue);
        RuleFor(x => x.Sla).IsInEnum().When(x => x.Sla.HasValue);
        RuleFor(x => x.Sort).IsInEnum();
    }
}

public sealed class AssignRequestValidator : AbstractValidator<AssignRequest>
{
    public AssignRequestValidator() => RuleFor(x => x.AssigneeId).NotEmpty().WithMessage("Elige a quién asignar.");
}

public sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty().WithMessage("Elige la cola de destino.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Indica el motivo de la transferencia.").MaximumLength(Ticket.ReasonMaxLength);
    }
}

public sealed class TransitionRequestValidator : AbstractValidator<TransitionRequest>
{
    public TransitionRequestValidator()
    {
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Indica el motivo.")
            .When(x => x.Action is TransitionAction.Hold or TransitionAction.Reopen or TransitionAction.Cancel);
        RuleFor(x => x.Reason).MaximumLength(Ticket.ReasonMaxLength);
        RuleFor(x => x.ResolutionSummary)
            .NotEmpty().WithMessage("Escribe la solución que verá el cliente.")
            .When(x => x.Action == TransitionAction.Resolve);
        RuleFor(x => x.ResolutionSummary).MaximumLength(Ticket.SummaryMaxLength);
    }
}

public sealed class ClassifyRequestValidator : AbstractValidator<ClassifyRequest>
{
    public ClassifyRequestValidator()
    {
        RuleFor(x => x.Impact).IsInEnum();
        RuleFor(x => x.Urgency).IsInEnum();
    }
}

public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator() =>
        RuleFor(x => x.Body).NotEmpty().WithMessage("Escribe un comentario.")
            .MaximumLength(TicketComment.BodyMaxLength).WithMessage($"Máximo {TicketComment.BodyMaxLength} caracteres.");
}

public sealed class SurveyRequestValidator : AbstractValidator<SurveyRequest>
{
    public SurveyRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("La calificación va de 1 a 5.");
        RuleFor(x => x.Comment).MaximumLength(SatisfactionSurvey.CommentMaxLength);
    }
}

public sealed class CorrectSuggestionRequestValidator : AbstractValidator<CorrectSuggestionRequest>
{
    public CorrectSuggestionRequestValidator()
    {
        RuleFor(x => x).Must(x => x.CategoryId.HasValue || x.TeamId.HasValue || x.Urgency.HasValue)
            .WithName("correction").WithMessage("Indica al menos una corrección.");
        RuleFor(x => x.Urgency).IsInEnum().When(x => x.Urgency.HasValue);
    }
}

public sealed class PushSuggestionRequestValidator : AbstractValidator<PushSuggestionRequest>
{
    public PushSuggestionRequestValidator()
    {
        RuleFor(x => x.Provider).NotEmpty().MaximumLength(60);
        RuleFor(x => x.ModelVersion).NotEmpty().MaximumLength(60);
        RuleFor(x => x.CategoryCode).MaximumLength(TicketSuggestion.CodeMaxLength).Matches("^[a-z0-9_]+$").When(x => x.CategoryCode is not null);
        RuleFor(x => x.TeamCode).MaximumLength(TicketSuggestion.CodeMaxLength).Matches("^[a-z0-9_]+$").When(x => x.TeamCode is not null);
        RuleFor(x => x.Urgency).IsInEnum().When(x => x.Urgency.HasValue);
        RuleFor(x => x.CategoryConfidence).InclusiveBetween(0m, 1m).When(x => x.CategoryConfidence.HasValue);
        RuleFor(x => x.TeamConfidence).InclusiveBetween(0m, 1m).When(x => x.TeamConfidence.HasValue);
        RuleFor(x => x.Decision).Must(d => d is "auto_route" or "human_review").When(x => x.Decision is not null)
            .WithMessage("decision debe ser auto_route o human_review.");
        RuleFor(x => x).Must(x => x.CategoryCode is not null || x.TeamCode is not null || x.Urgency.HasValue)
            .WithName("suggestion").WithMessage("La sugerencia no tiene valores.");
    }
}

public sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.ServiceTier).IsInEnum();
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Matches(@"^\+?[0-9 ]{7,20}$").WithMessage("Teléfono no válido.").When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    private static readonly string[] Roles = ["Admin", "Supervisor", "Agent", "Customer"];

    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty()
            .Length(PasswordRules.MinLength, PasswordRules.MaxLength)
            .WithMessage($"La contraseña debe tener entre {PasswordRules.MinLength} y {PasswordRules.MaxLength} caracteres.");
        RuleFor(x => x.Role).Must(r => Roles.Contains(r)).WithMessage("Rol no válido.");
        RuleFor(x => x.CustomerId).NotEmpty().When(x => x.Role == "Customer").WithMessage("Un usuario cliente necesita un cliente.");
        RuleFor(x => x.CustomerId).Empty().When(x => x.Role != "Customer").WithMessage("Solo los usuarios cliente pertenecen a un cliente.");
        RuleFor(x => x.TeamIds).Empty().When(x => x.Role == "Customer").WithMessage("Los clientes no pertenecen a colas.");
    }
}

public sealed class SetTeamMembersRequestValidator : AbstractValidator<SetTeamMembersRequest>
{
    public SetTeamMembersRequestValidator()
    {
        RuleFor(x => x.Members).Must(m => m.Select(i => i.UserId).Distinct().Count() == m.Count).WithMessage("Hay miembros repetidos.");
        RuleForEach(x => x.Members).ChildRules(m => m.RuleFor(i => i.UserId).NotEmpty());
    }
}

public sealed class UpdateSlaPolicyRequestValidator : AbstractValidator<UpdateSlaPolicyRequest>
{
    public UpdateSlaPolicyRequestValidator()
    {
        RuleFor(x => x.FirstResponseMinutes).GreaterThan(0).LessThanOrEqualTo(60 * 24 * 30);
        RuleFor(x => x.ResolutionMinutes).GreaterThanOrEqualTo(x => x.FirstResponseMinutes)
            .WithMessage("La resolución no puede ser menor que la primera respuesta.")
            .LessThanOrEqualTo(60 * 24 * 90);
        RuleFor(x => x.WarningPercent).InclusiveBetween(SlaPolicy.MinWarningPercent, SlaPolicy.MaxWarningPercent);
    }
}

public sealed class CreateHolidayRequestValidator : AbstractValidator<CreateHolidayRequest>
{
    public CreateHolidayRequestValidator()
    {
        RuleFor(x => x.Date).Must(d => d.Year is >= 2020 and <= 2100).WithMessage("Fecha fuera de rango.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.LegalBasis).NotEmpty().WithMessage("Indica la base legal (ley, decreto o acuerdo).").MaximumLength(200);
    }
}

public sealed class ReportQueryValidator : AbstractValidator<ReportQuery>
{
    public const int MaxRangeDays = 366;

    public ReportQueryValidator()
    {
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithName("range").WithMessage("La fecha inicial debe ser anterior a la final.");
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.To.Value.DayNumber - x.From.Value.DayNumber <= MaxRangeDays)
            .WithName("range").WithMessage($"El rango máximo es de {MaxRangeDays} días.");
    }
}

/// <summary>Validación previa de archivos en el front (la definitiva la hace el servidor con la firma binaria).</summary>
public static class AttachmentClientRules
{
    public static bool IsAllowedExtension(string fileName) =>
        AttachmentRules.AllowedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public static long MaxBytes => AttachmentRules.MaxBytes;
}
