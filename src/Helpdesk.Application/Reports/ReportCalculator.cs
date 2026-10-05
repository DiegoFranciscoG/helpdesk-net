using Helpdesk.Application.Abstractions;
using Helpdesk.Contracts.Reports;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Application.Reports;

/// <summary>
/// Cálculo puro de indicadores (R15). Los tiempos se miden en minutos hábiles (ya netos de pausas) y se
/// expresan en horas. Cumplimiento = % dentro del objetivo sobre lo medible en el periodo.
/// </summary>
public static class ReportCalculator
{
    public static ReportOverviewDto Overview(IReadOnlyList<TicketReportRow> rows, DateOnly from, DateOnly to, DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(zone);
        var created = rows.Where(r => In(r.CreatedAt, fromUtc, toUtc)).ToList();
        var resolved = rows.Where(r => r.ResolvedAt is { } at && In(at, fromUtc, toUtc)).ToList();
        var open = rows.Where(r => IsOpen(r.Status)).ToList();
        var surveyed = resolved.Where(r => r.SurveyRating is not null).ToList();

        var byPriority = Enum.GetValues<Priority>()
            .Select(p => new PriorityReportRow(
                p,
                created.Count(r => r.Priority == p),
                resolved.Count(r => r.Priority == p),
                FirstResponseCompliance(created.Where(r => r.Priority == p)),
                ResolutionCompliance(resolved.Where(r => r.Priority == p)),
                AverageHours(resolved.Where(r => r.Priority == p).Select(r => r.ResolutionBusinessMinutes))))
            .ToList();

        var byCategory = created
            .GroupBy(r => r.CategoryName ?? "Sin categoría")
            .Select(g => new CategoryReportRow(g.Key, g.Count()))
            .OrderByDescending(r => r.Created)
            .ThenBy(r => r.Category, StringComparer.Ordinal)
            .ToList();

        var daily = new List<DailyReportRow>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            daily.Add(new DailyReportRow(
                day,
                created.Count(r => LocalDate(r.CreatedAt, zone) == day),
                resolved.Count(r => LocalDate(r.ResolvedAt!.Value, zone) == day)));
        }

        return new ReportOverviewDto(
            from,
            to,
            created.Count,
            resolved.Count,
            open.Count,
            open.Count(IsBreached),
            AverageHours(created.Select(r => r.FirstResponseBusinessMinutes)),
            AverageHours(resolved.Select(r => r.ResolutionBusinessMinutes)),
            FirstResponseCompliance(created),
            ResolutionCompliance(resolved),
            surveyed.Count == 0 ? null : Math.Round(surveyed.Average(r => r.SurveyRating!.Value), 2),
            Percent(surveyed.Count(r => r.SurveyRating >= 4), surveyed.Count),
            surveyed.Count,
            byPriority,
            byCategory,
            daily);
    }

    public static IReadOnlyList<AgentWorkloadRow> AgentWorkload(IReadOnlyList<TicketReportRow> rows, IReadOnlyDictionary<Guid, string> agents, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(agents);
        return [.. agents
            .Select(agent =>
            {
                var mine = rows.Where(r => r.AssigneeId == agent.Key).ToList();
                var resolved = mine.Where(r => r.ResolvedAt is { } at && In(at, fromUtc, toUtc)).ToList();
                var rated = resolved.Where(r => r.SurveyRating is not null).ToList();
                return new AgentWorkloadRow(
                    agent.Key,
                    agent.Value,
                    mine.Count(r => r.Status == TicketStatus.Assigned),
                    mine.Count(r => r.Status == TicketStatus.InProgress),
                    mine.Count(r => r.Status == TicketStatus.OnHold),
                    resolved.Count,
                    AverageHours(resolved.Select(r => r.ResolutionBusinessMinutes)),
                    ResolutionCompliance(resolved),
                    rated.Count == 0 ? null : Math.Round(rated.Average(r => r.SurveyRating!.Value), 2));
            })
            .OrderByDescending(r => r.Assigned + r.InProgress + r.OnHold)
            .ThenBy(r => r.AgentName, StringComparer.Ordinal)];
    }

    /// <summary>Respondidos a tiempo / (respondidos + vencidos sin respuesta).</summary>
    public static double? FirstResponseCompliance(IEnumerable<TicketReportRow> rows)
    {
        var measurable = rows.Where(r => r.FirstRespondedAt is not null || r.FirstResponseBreached).ToList();
        return Percent(measurable.Count(r => r.FirstRespondedAt is not null && !r.FirstResponseBreached), measurable.Count);
    }

    /// <summary>Resueltos dentro del objetivo / resueltos en el periodo.</summary>
    public static double? ResolutionCompliance(IEnumerable<TicketReportRow> resolved)
    {
        var list = resolved.ToList();
        return Percent(list.Count(r => !r.ResolutionBreached), list.Count);
    }

    public static double? AverageHours(IEnumerable<int?> minutes)
    {
        var values = minutes.Where(m => m is not null).Select(m => m!.Value).ToList();
        return values.Count == 0 ? null : Math.Round(values.Average() / 60.0, 2);
    }

    public static bool IsOpen(TicketStatus status) => status is TicketStatus.New or TicketStatus.Assigned or TicketStatus.InProgress or TicketStatus.OnHold;

    private static bool IsBreached(TicketReportRow r) => r.ResolutionBreached || (r.FirstRespondedAt is null && r.FirstResponseBreached);

    private static double? Percent(int part, int total) => total == 0 ? null : Math.Round(100.0 * part / total, 1);

    private static bool In(DateTimeOffset instant, DateTimeOffset fromUtc, DateTimeOffset toUtc) => instant >= fromUtc && instant < toUtc;

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
