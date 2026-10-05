using Helpdesk.Domain.Tickets;

namespace Helpdesk.Contracts.Reports;

public sealed class ReportQuery
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public Guid? TeamId { get; set; }
}

public sealed record PriorityReportRow(
    Priority Priority,
    int Created,
    int Resolved,
    double? FirstResponseCompliancePercent,
    double? ResolutionCompliancePercent,
    double? AverageResolutionBusinessHours);

public sealed record CategoryReportRow(string Category, int Created);

public sealed record DailyReportRow(DateOnly Date, int Created, int Resolved);

/// <summary>Indicadores del periodo: tiempos medios en horas hábiles, cumplimiento de SLA y satisfacción (R15).</summary>
public sealed record ReportOverviewDto(
    DateOnly From,
    DateOnly To,
    int Created,
    int Resolved,
    int OpenNow,
    int BreachedOpenNow,
    double? AverageFirstResponseBusinessHours,
    double? AverageResolutionBusinessHours,
    double? FirstResponseCompliancePercent,
    double? ResolutionCompliancePercent,
    double? CsatAverage,
    double? CsatPercent,
    int SurveyCount,
    IReadOnlyList<PriorityReportRow> ByPriority,
    IReadOnlyList<CategoryReportRow> ByCategory,
    IReadOnlyList<DailyReportRow> Daily);

/// <summary>Carga actual y desempeño del periodo por agente.</summary>
public sealed record AgentWorkloadRow(
    Guid AgentId,
    string AgentName,
    int Assigned,
    int InProgress,
    int OnHold,
    int ResolvedInPeriod,
    double? AverageResolutionBusinessHours,
    double? ResolutionCompliancePercent,
    double? CsatAverage);
