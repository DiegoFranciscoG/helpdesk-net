using Helpdesk.Domain.Common;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Domain.Sla;

/// <summary>Regla de SLA: objetivos de primera respuesta y resolución por prioridad y nivel de servicio (R2).</summary>
public sealed class SlaPolicy : Entity
{
    public const int MinWarningPercent = 50;
    public const int MaxWarningPercent = 95;

    private SlaPolicy()
    {
        Name = string.Empty;
    }

    public SlaPolicy(
        Guid id,
        string name,
        Priority priority,
        ServiceTier? serviceTier,
        int firstResponseMinutes,
        int resolutionMinutes,
        int warningPercent,
        Guid calendarId)
    {
        Id = id;
        Name = name;
        Priority = priority;
        ServiceTier = serviceTier;
        CalendarId = calendarId;
        IsActive = true;
        ChangeTargets(firstResponseMinutes, resolutionMinutes, warningPercent);
    }

    public string Name { get; private set; }

    public Priority Priority { get; private set; }

    /// <summary>Nivel de servicio al que aplica; <c>null</c> = cualquiera.</summary>
    public ServiceTier? ServiceTier { get; private set; }

    public int FirstResponseMinutes { get; private set; }

    public int ResolutionMinutes { get; private set; }

    public int WarningPercent { get; private set; }

    public Guid CalendarId { get; private set; }

    public bool IsActive { get; private set; }

    public void ChangeTargets(int firstResponseMinutes, int resolutionMinutes, int warningPercent)
    {
        if (firstResponseMinutes <= 0)
        {
            throw new DomainException("invalid_sla_target", "El objetivo de primera respuesta debe ser mayor que cero.");
        }

        if (resolutionMinutes < firstResponseMinutes)
        {
            throw new DomainException("invalid_sla_target", "El objetivo de resolución no puede ser menor que el de primera respuesta.");
        }

        if (warningPercent is < MinWarningPercent or > MaxWarningPercent)
        {
            throw new DomainException("invalid_sla_target", $"El aviso debe estar entre {MinWarningPercent} % y {MaxWarningPercent} %.");
        }

        FirstResponseMinutes = firstResponseMinutes;
        ResolutionMinutes = resolutionMinutes;
        WarningPercent = warningPercent;
    }

    public void ChangeCalendar(Guid calendarId) => CalendarId = calendarId;
}

/// <summary>Objetivos de una regla ya resueltos junto con su calendario, listos para que el ticket calcule vencimientos.</summary>
public sealed record SlaTerms(Guid PolicyId, int FirstResponseMinutes, int ResolutionMinutes, int WarningPercent, ISlaCalendar Calendar)
{
    public static SlaTerms From(SlaPolicy policy, ISlaCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new SlaTerms(policy.Id, policy.FirstResponseMinutes, policy.ResolutionMinutes, policy.WarningPercent, calendar);
    }

    public int WarningMinutes(int targetMinutes) => (int)Math.Ceiling(targetMinutes * WarningPercent / 100.0);
}

/// <summary>Elige la regla activa más específica: primero la del nivel del cliente y si no, la general (R2).</summary>
public static class SlaPolicySelector
{
    public static SlaPolicy Select(IEnumerable<SlaPolicy> policies, Priority priority, ServiceTier tier)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var candidates = policies.Where(p => p.IsActive && p.Priority == priority).ToList();
        return candidates.FirstOrDefault(p => p.ServiceTier == tier)
            ?? candidates.FirstOrDefault(p => p.ServiceTier is null)
            ?? throw new DomainException("sla_policy_missing", $"No hay una regla de SLA activa para la prioridad {priority}.");
    }
}
