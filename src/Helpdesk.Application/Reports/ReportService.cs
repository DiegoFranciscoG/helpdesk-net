using System.Globalization;
using System.Text;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Sla;
using Helpdesk.Contracts.Reports;
using Helpdesk.Domain.Tickets;
using Microsoft.Extensions.Options;

namespace Helpdesk.Application.Reports;

/// <summary>Reportes para supervisión: indicadores del periodo, carga por agente y exportación CSV sin datos personales.</summary>
public sealed class ReportService(ITicketQueries queries, IUserDirectory users, IOptions<HelpdeskOptions> options, TimeProvider clock)
{
    public const int DefaultRangeDays = 30;

    public async Task<ReportOverviewDto> OverviewAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var range = Resolve(query);
        var rows = await queries.ReportRowsAsync(range.FromUtc, range.ToUtc, query.TeamId, cancellationToken);
        return ReportCalculator.Overview(rows, range.From, range.To, range.FromUtc, range.ToUtc, range.Zone);
    }

    public async Task<IReadOnlyList<AgentWorkloadRow>> AgentsAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var range = Resolve(query);
        var rows = await queries.ReportRowsAsync(range.FromUtc, range.ToUtc, query.TeamId, cancellationToken);
        var staff = await users.ListStaffAsync(query.TeamId, cancellationToken);
        var agents = staff.Where(s => s.Role == Domain.Common.ActorRole.Agent || rows.Any(r => r.AssigneeId == s.Id))
            .ToDictionary(s => s.Id, s => s.FullName);
        return ReportCalculator.AgentWorkload(rows, agents, range.FromUtc, range.ToUtc);
    }

    /// <summary>
    /// CSV de tickets creados en el periodo. Identifica al cliente solo por código y neutraliza fórmulas
    /// (inyección CSV: celdas que empiezan con = + - @).
    /// </summary>
    public async Task<string> ExportCsvAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var range = Resolve(query);
        var rows = await queries.ReportRowsAsync(range.FromUtc, range.ToUtc, query.TeamId, cancellationToken);
        var assignees = await users.GetNamesAsync(rows.Where(r => r.AssigneeId is not null).Select(r => r.AssigneeId!.Value), cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine("numero,creado,estado,prioridad,categoria,cola,asignado,cliente,primera_respuesta_min_habiles,resolucion_min_habiles,respuesta_incumplida,resolucion_incumplida,csat");
        foreach (var r in rows.Where(r => r.CreatedAt >= range.FromUtc && r.CreatedAt < range.ToUtc).OrderBy(r => r.Number))
        {
            string[] cells =
            [
                Ticket.FormatNumber(r.Number),
                TimeZoneInfo.ConvertTime(r.CreatedAt, range.Zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Snake(r.Status.ToString()),
                ((int)r.Priority).ToString(CultureInfo.InvariantCulture),
                r.CategoryName ?? string.Empty,
                r.TeamName,
                r.AssigneeId is { } a ? assignees.GetValueOrDefault(a, string.Empty) : string.Empty,
                r.CustomerCode,
                r.FirstResponseBusinessMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                r.ResolutionBusinessMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                r.FirstResponseBreached ? "si" : "no",
                r.ResolutionBreached ? "si" : "no",
                r.SurveyRating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ];
            csv.AppendLine(string.Join(',', cells.Select(Escape)));
        }

        return csv.ToString();
    }

    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var safe = value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal) ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : safe;
    }

    private ResolvedRange Resolve(ReportQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-(DefaultRangeDays - 1));
        return new ResolvedRange(from, to, StartOfDayUtc(from, zone), StartOfDayUtc(to.AddDays(1), zone), zone);
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly day, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

    private static string Snake(string name) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    private sealed record ResolvedRange(DateOnly From, DateOnly To, DateTimeOffset FromUtc, DateTimeOffset ToUtc, TimeZoneInfo Zone);
}
