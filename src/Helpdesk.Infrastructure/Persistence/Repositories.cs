using System.Globalization;
using Helpdesk.Application.Abstractions;
using Helpdesk.Contracts.Common;
using Helpdesk.Domain.Catalog;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Persistence;

internal sealed class TicketRepository(HelpdeskDbContext db) : ITicketRepository
{
    private static readonly TicketStatus[] Running = [TicketStatus.New, TicketStatus.Assigned, TicketStatus.InProgress];
    private static readonly TicketStatus[] Terminal = [TicketStatus.Closed, TicketStatus.Cancelled];

    public Task<Ticket?> GetAsync(Guid id, bool includeDetails, CancellationToken cancellationToken) =>
        Query(includeDetails).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<Ticket?> GetByAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        var ticketId = await db.TicketAttachments.Where(a => a.Id == attachmentId).Select(a => (Guid?)a.TicketId).FirstOrDefaultAsync(cancellationToken);
        return ticketId is null ? null : await GetAsync(ticketId.Value, includeDetails: true, cancellationToken);
    }

    public void Add(Ticket ticket) => db.Tickets.Add(ticket);

    public async Task<IReadOnlyList<Ticket>> GetDueForSlaEvaluationAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await db.Tickets
            .Include(t => t.Survey)
            .Where(t => !Terminal.Contains(t.Status) && (
                (t.FirstRespondedAt == null && ((!t.ResponseWarningSent && t.FirstResponseWarningAt <= now) || (!t.FirstResponseBreached && t.FirstResponseDueAt < now)))
                || (Running.Contains(t.Status) && ((!t.ResolutionWarningSent && t.ResolutionWarningAt <= now) || (!t.ResolutionBreached && t.ResolutionDueAt < now)))
                || (t.Status == TicketStatus.Resolved && t.AutoCloseAt != null && t.AutoCloseAt <= now)))
            .OrderBy(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetUnclassifiedWithoutSuggestionAsync(DateTimeOffset since, int limit, CancellationToken cancellationToken) =>
        await db.Tickets
            .Where(t => t.CategoryId == null && !Terminal.Contains(t.Status) && t.CreatedAt >= since)
            .Where(t => !db.TicketSuggestions.Any(s => s.TicketId == t.Id))
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    private IQueryable<Ticket> Query(bool includeDetails)
    {
        IQueryable<Ticket> query = db.Tickets.Include(t => t.Survey);
        if (includeDetails)
        {
            query = query.Include(t => t.History).Include(t => t.Comments).Include(t => t.Attachments).AsSplitQuery();
        }

        return query;
    }
}

internal sealed class SuggestionRepository(HelpdeskDbContext db) : ISuggestionRepository
{
    public Task<TicketSuggestion?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.TicketSuggestions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TicketSuggestion>> ListForTicketAsync(Guid ticketId, CancellationToken cancellationToken) =>
        await db.TicketSuggestions.Where(s => s.TicketId == ticketId).OrderByDescending(s => s.CreatedAt).Take(20).ToListAsync(cancellationToken);

    public Task<bool> HasPendingAsync(Guid ticketId, CancellationToken cancellationToken) =>
        db.TicketSuggestions.AnyAsync(s => s.TicketId == ticketId && s.Status == SuggestionStatus.Pending, cancellationToken);

    public void Add(TicketSuggestion suggestion) => db.TicketSuggestions.Add(suggestion);
}

internal sealed class CatalogRepository(HelpdeskDbContext db) : ICatalogRepository
{
    public async Task<IReadOnlyList<Team>> ListTeamsAsync(bool includeMembers, CancellationToken cancellationToken) =>
        includeMembers
            ? await db.Teams.Include(t => t.Members).ToListAsync(cancellationToken)
            : await db.Teams.ToListAsync(cancellationToken);

    public Task<Team?> GetTeamAsync(Guid id, CancellationToken cancellationToken) =>
        db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        await db.Categories.ToListAsync(cancellationToken);

    public Task<Category?> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
}

internal sealed class CustomerRepository(HelpdeskDbContext db) : ICustomerRepository
{
    public Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<PagedResult<Customer>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{LikeEscape.Escape(search)}%";
            query = query.Where(c => EF.Functions.ILike(c.DisplayName, pattern, LikeEscape.Character) || EF.Functions.ILike(c.Code, pattern, LikeEscape.Character));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.Code).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<Customer>(items, page, pageSize, total);
    }

    public async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        var codes = await db.Customers.Select(c => c.Code).ToListAsync(cancellationToken);
        var max = codes.Select(c => int.TryParse(c.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"CLI-{max + 1:D5}";
    }

    public void Add(Customer customer) => db.Customers.Add(customer);
}

internal sealed class SlaRepository(HelpdeskDbContext db) : ISlaRepository
{
    public async Task<IReadOnlyList<SlaPolicy>> ListPoliciesAsync(CancellationToken cancellationToken) =>
        await db.SlaPolicies.ToListAsync(cancellationToken);

    public Task<SlaPolicy?> GetPolicyAsync(Guid id, CancellationToken cancellationToken) =>
        db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BusinessCalendar>> ListCalendarsAsync(CancellationToken cancellationToken) =>
        await db.BusinessCalendars.Include(c => c.Hours).Include(c => c.Holidays).AsSplitQuery().ToListAsync(cancellationToken);

    public Task<BusinessCalendar?> GetCalendarAsync(Guid id, CancellationToken cancellationToken) =>
        db.BusinessCalendars.Include(c => c.Hours).Include(c => c.Holidays).AsSplitQuery().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<BusinessCalendar?> GetCalendarByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.BusinessCalendars.FirstOrDefaultAsync(c => c.Code == code, cancellationToken);
}

internal sealed class NotificationRepository(HelpdeskDbContext db) : INotificationRepository
{
    public void Add(Notification notification) => db.Notifications.Add(notification);

    public async Task<IReadOnlyList<Notification>> ListAsync(Guid userId, bool unreadOnly, int limit, CancellationToken cancellationToken) =>
        await db.Notifications
            .Where(n => n.UserId == userId && (!unreadOnly || !n.IsRead))
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), cancellationToken);
}

/// <summary>Escapa comodines de LIKE para que la búsqueda del usuario sea literal (consulta parametrizada).</summary>
internal static class LikeEscape
{
    public const string Character = "\\";

    public static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
