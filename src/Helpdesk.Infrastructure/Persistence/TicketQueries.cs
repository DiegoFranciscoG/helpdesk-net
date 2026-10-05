using System.Globalization;
using System.Text.RegularExpressions;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Security;
using Helpdesk.Application.Tickets;
using Helpdesk.Contracts.Common;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Persistence;

/// <summary>Consultas de solo lectura con proyecciones (sin cargar agregados) y filtros de visibilidad por rol.</summary>
internal sealed partial class TicketQueries(HelpdeskDbContext db) : ITicketQueries
{
    private static readonly TicketStatus[] Open = [TicketStatus.New, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.OnHold];
    private static readonly TicketStatus[] Running = [TicketStatus.New, TicketStatus.Assigned, TicketStatus.InProgress];
    private static readonly TicketStatus[] Finished = [TicketStatus.Resolved, TicketStatus.Closed];
    private static readonly TicketStatus[] Terminal = [TicketStatus.Closed, TicketStatus.Cancelled];

    public async Task<PagedResult<TicketListItemDto>> ListAsync(TicketQuery query, AccessContext access, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(access);

        var tickets = Visible(db.Tickets.AsNoTracking(), access);
        tickets = Filter(tickets, query, access, now);

        var total = await tickets.CountAsync(cancellationToken);
        var projected =
            from t in tickets
            join team in db.Teams on t.TeamId equals team.Id
            join customer in db.Customers on t.CustomerId equals customer.Id
            from category in db.Categories.Where(c => c.Id == t.CategoryId).DefaultIfEmpty()
            from assignee in db.Users.Where(u => u.Id == t.AssigneeId).DefaultIfEmpty()
            select new
            {
                t.Id,
                t.Number,
                t.Title,
                t.Status,
                t.Priority,
                t.Impact,
                t.Urgency,
                CategoryName = category == null ? null : category.Name,
                t.TeamId,
                TeamName = team.Name,
                t.AssigneeId,
                AssigneeName = assignee == null ? null : assignee.FullName,
                CustomerCode = customer.Code,
                CustomerName = customer.DisplayName,
                t.CreatedAt,
                t.FirstRespondedAt,
                t.FirstResponseWarningAt,
                t.FirstResponseDueAt,
                t.FirstResponseBreached,
                t.ResolutionWarningAt,
                t.ResolutionDueAt,
                t.ResolutionBreached,
                t.EscalationLevel,
            };

        // El orden se aplica después de los joins: ordenar antes no se conserva en la consulta externa.
        projected = query.Sort switch
        {
            TicketSort.Priority => projected.OrderBy(r => r.Priority).ThenBy(r => r.ResolutionDueAt).ThenBy(r => r.Number),
            TicketSort.DueDate => projected.OrderBy(r => r.ResolutionDueAt).ThenBy(r => r.Number),
            _ => projected.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Number),
        };

        var rows = await projected.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);

        var items = rows.Select(r => new TicketListItemDto(
            r.Id,
            Ticket.FormatNumber(r.Number),
            r.Title,
            r.Status,
            r.Priority,
            r.Impact,
            r.Urgency,
            r.CategoryName,
            r.TeamId,
            r.TeamName,
            r.AssigneeId,
            r.AssigneeName,
            r.CustomerCode,
            r.CustomerName,
            r.CreatedAt,
            r.FirstResponseDueAt,
            r.ResolutionDueAt,
            SlaStateCalculator.Calculate(r.Status, r.FirstRespondedAt, r.FirstResponseWarningAt, r.FirstResponseDueAt, r.FirstResponseBreached, r.ResolutionWarningAt, r.ResolutionDueAt, r.ResolutionBreached, now),
            r.EscalationLevel)).ToList();

        return new PagedResult<TicketListItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<UnclassifiedTicketDto>> ListUnclassifiedAsync(int limit, CancellationToken cancellationToken)
    {
        var rows = await db.Tickets.AsNoTracking()
            .Where(t => t.CategoryId == null && !Terminal.Contains(t.Status))
            .Where(t => !db.TicketSuggestions.Any(s => s.TicketId == t.Id && s.Status == SuggestionStatus.Pending))
            .OrderBy(t => t.CreatedAt)
            .Take(limit)
            .Select(t => new { t.Id, t.Number, t.Title, t.Description, t.Channel, t.CreatedAt })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new UnclassifiedTicketDto(r.Id, Ticket.FormatNumber(r.Number), r.Title, r.Description, r.Channel, r.CreatedAt))];
    }

    public async Task<IReadOnlyList<TicketReportRow>> ReportRowsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? teamId, CancellationToken cancellationToken)
    {
        var tickets = db.Tickets.AsNoTracking().Where(t =>
            (t.CreatedAt >= fromUtc && t.CreatedAt < toUtc)
            || (t.ResolvedAt != null && t.ResolvedAt >= fromUtc && t.ResolvedAt < toUtc)
            || Open.Contains(t.Status));
        if (teamId is { } team)
        {
            tickets = tickets.Where(t => t.TeamId == team);
        }

        return await (
            from t in tickets
            join teamRow in db.Teams on t.TeamId equals teamRow.Id
            join customer in db.Customers on t.CustomerId equals customer.Id
            from category in db.Categories.Where(c => c.Id == t.CategoryId).DefaultIfEmpty()
            from survey in db.SatisfactionSurveys.Where(s => s.TicketId == t.Id).DefaultIfEmpty()
            select new TicketReportRow(
                t.Id,
                t.Number,
                t.CreatedAt,
                t.Status,
                t.Priority,
                category == null ? null : category.Name,
                t.TeamId,
                teamRow.Name,
                t.AssigneeId,
                customer.Code,
                t.FirstRespondedAt,
                t.FirstResponseBreached,
                t.FirstResponseBusinessMinutes,
                t.ResolvedAt,
                t.ResolutionBreached,
                t.ResolutionBusinessMinutes,
                survey == null ? null : (int?)survey.Rating)).ToListAsync(cancellationToken);
    }

    /// <summary>Filtro de visibilidad en SQL equivalente a <see cref="TicketAccessPolicy.CanView"/>.</summary>
    private static IQueryable<Ticket> Visible(IQueryable<Ticket> tickets, AccessContext access)
    {
        var teamIds = access.TeamIds.ToArray();
        var userId = access.UserId;
        return access.Role switch
        {
            ActorRole.Admin or ActorRole.Supervisor => tickets,
            ActorRole.Agent => tickets.Where(t => teamIds.Contains(t.TeamId) || t.AssigneeId == userId),
            ActorRole.Customer when access.CustomerId is { } customerId => tickets.Where(t => t.CustomerId == customerId),
            _ => tickets.Where(_ => false),
        };
    }

    private static IQueryable<Ticket> Filter(IQueryable<Ticket> tickets, TicketQuery query, AccessContext access, DateTimeOffset now)
    {
        if (query.Status is { } status)
        {
            tickets = tickets.Where(t => t.Status == status);
        }
        else if (query.OpenOnly)
        {
            tickets = tickets.Where(t => Open.Contains(t.Status));
        }

        if (query.Priority is { } priority)
        {
            tickets = tickets.Where(t => t.Priority == priority);
        }

        if (query.TeamId is { } teamId)
        {
            tickets = tickets.Where(t => t.TeamId == teamId);
        }

        if (query.AssignedToMe)
        {
            tickets = tickets.Where(t => t.AssigneeId == access.UserId);
        }
        else if (query.AssigneeId is { } assigneeId)
        {
            tickets = tickets.Where(t => t.AssigneeId == assigneeId);
        }

        if (query.CustomerId is { } customerId && access.IsStaff)
        {
            tickets = tickets.Where(t => t.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var number = NumberPattern().Match(term);
            if (number.Success && long.TryParse(number.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                tickets = tickets.Where(t => t.Number == n);
            }
            else
            {
                var pattern = $"%{LikeEscape.Escape(term)}%";
                tickets = tickets.Where(t => EF.Functions.ILike(t.Title, pattern, LikeEscape.Character));
            }
        }

        return query.Sla switch
        {
            SlaState.Breached => tickets.Where(t => Open.Contains(t.Status) && (t.FirstResponseBreached || t.ResolutionBreached
                || (t.FirstRespondedAt == null && t.FirstResponseDueAt < now)
                || (Running.Contains(t.Status) && t.ResolutionDueAt < now))),
            SlaState.AtRisk => tickets.Where(t => Running.Contains(t.Status) && !t.FirstResponseBreached && !t.ResolutionBreached
                && !(t.FirstRespondedAt == null && t.FirstResponseDueAt < now) && t.ResolutionDueAt >= now
                && ((t.FirstRespondedAt == null && t.FirstResponseWarningAt <= now) || t.ResolutionWarningAt <= now)),
            SlaState.Paused => tickets.Where(t => t.Status == TicketStatus.OnHold),
            SlaState.OnTrack => tickets.Where(t => Running.Contains(t.Status) && !t.FirstResponseBreached && !t.ResolutionBreached
                && (t.FirstRespondedAt != null || t.FirstResponseWarningAt > now) && t.ResolutionWarningAt > now),
            SlaState.Met => tickets.Where(t => Finished.Contains(t.Status) && !t.FirstResponseBreached && !t.ResolutionBreached),
            SlaState.NotApplicable => tickets.Where(t => t.Status == TicketStatus.Cancelled),
            _ => tickets,
        };
    }

    [GeneratedRegex(@"^(?:HD-)?0*(?<n>\d{1,12})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NumberPattern();
}
