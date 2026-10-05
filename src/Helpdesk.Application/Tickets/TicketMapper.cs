using Helpdesk.Application.Security;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Application.Tickets;

/// <summary>Nombres resueltos fuera del agregado (Identity, catálogo y cliente) para armar los DTO.</summary>
public sealed record TicketLookups(
    IReadOnlyDictionary<Guid, string> UserNames,
    IReadOnlySet<Guid> CustomerUserIds,
    string TeamName,
    string? CategoryName,
    Customer Customer,
    string PolicyName)
{
    public string NameOf(Guid? userId, string fallback = "Sistema") =>
        userId is { } id && UserNames.TryGetValue(id, out var name) ? name : fallback;
}

/// <summary>Mapeo explícito dominio → DTO (sin AutoMapper). Filtra lo interno para los clientes (OWASP API3).</summary>
public static class TicketMapper
{
    public static TicketDetailDto ToDetail(Ticket ticket, TicketLookups lookups, AccessContext access, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(access);
        var seeInternal = TicketAccessPolicy.CanSeeInternal(access);

        var comments = ticket.Comments
            .Where(c => seeInternal || !c.IsInternal)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, c.AuthorId, lookups.NameOf(c.AuthorId, "Usuario"), !lookups.CustomerUserIds.Contains(c.AuthorId), c.Body, c.IsInternal, c.CreatedAt))
            .ToList();

        var attachments = ticket.Attachments
            .Where(a => seeInternal || !a.IsInternal)
            .OrderBy(a => a.CreatedAt)
            .Select(a => ToDto(a, lookups))
            .ToList();

        var history = ticket.History
            .OrderBy(h => h.ChangedAt)
            .Select(h => new StatusChangeDto(h.FromStatus, h.ToStatus, h.ChangedById is null ? "Sistema" : lookups.NameOf(h.ChangedById, "Usuario"), h.Reason, h.ChangedAt))
            .ToList();

        return new TicketDetailDto(
            ticket.Id,
            ticket.DisplayNumber,
            ticket.Title,
            ticket.Description,
            ticket.Status,
            ticket.Channel,
            ticket.Impact,
            ticket.Urgency,
            ticket.Priority,
            ticket.CategoryId,
            lookups.CategoryName,
            ticket.TeamId,
            lookups.TeamName,
            ticket.AssigneeId,
            ticket.AssigneeId is null ? null : lookups.NameOf(ticket.AssigneeId, "Usuario"),
            ticket.CustomerId,
            lookups.Customer.Code,
            lookups.Customer.DisplayName,
            lookups.NameOf(ticket.CreatedById, "Usuario"),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.ResolutionSummary,
            ticket.ReopenCount,
            ticket.AutoCloseAt,
            ticket.ClosedAt,
            ToSla(ticket, lookups.PolicyName, now),
            comments,
            attachments,
            history,
            ticket.Survey is null ? null : new SurveyDto(ticket.Survey.Rating, ticket.Survey.Comment, ticket.Survey.SubmittedAt),
            TicketAccessPolicy.AllowedActions(ticket, access),
            ticket.Version);
    }

    public static SlaInfoDto ToSla(Ticket ticket, string policyName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new SlaInfoDto(
            SlaStateCalculator.Calculate(ticket, now),
            policyName,
            ticket.FirstResponseDueAt,
            ticket.FirstRespondedAt,
            ticket.FirstResponseBreached,
            ticket.ResolutionDueAt,
            ticket.ResolvedAt,
            ticket.ResolutionBreached,
            ticket.SlaPausedAt is not null && !ticket.Status.IsTerminal(),
            ticket.SlaPausedBusinessMinutes,
            ticket.EscalationLevel);
    }

    public static AttachmentDto ToDto(TicketAttachment attachment, TicketLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(lookups);
        return new AttachmentDto(attachment.Id, attachment.OriginalFileName, attachment.ContentType, attachment.SizeBytes, attachment.IsInternal, lookups.NameOf(attachment.UploadedById, "Usuario"), attachment.CreatedAt);
    }
}
