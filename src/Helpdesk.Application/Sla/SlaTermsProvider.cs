using Helpdesk.Application.Abstractions;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;
using Microsoft.Extensions.Options;

namespace Helpdesk.Application.Sla;

/// <summary>Parámetros de negocio (no secretos) con valores por defecto razonables.</summary>
public sealed class HelpdeskOptions
{
    public const string SectionName = "Helpdesk";

    /// <summary>Zona horaria del negocio para reportes y calendarios (IANA).</summary>
    public string TimeZone { get; set; } = "America/Guayaquil";

    /// <summary>Calendario con el que se mide el cierre automático y los días hábiles de los reportes.</summary>
    public string OfficeCalendarCode { get; set; } = "ec-office";

    /// <summary>Días hábiles tras los que un ticket resuelto se cierra solo (R9).</summary>
    public int AutoCloseBusinessDays { get; set; } = 3;

    /// <summary>Minutos hábiles de una jornada (8 h, Código del Trabajo art. 47).</summary>
    public int BusinessDayMinutes { get; set; } = 480;
}

public interface ISlaTermsProvider
{
    Task<SlaTerms> ForPolicyAsync(Guid policyId, CancellationToken cancellationToken);

    Task<SlaTerms> SelectAsync(Priority priority, ServiceTier tier, CancellationToken cancellationToken);

    Task<string> PolicyNameAsync(Guid policyId, CancellationToken cancellationToken);

    Task<DateTimeOffset> AutoCloseAtAsync(DateTimeOffset resolvedAt, CancellationToken cancellationToken);

    void Invalidate();
}

/// <summary>
/// Resuelve reglas de SLA y calendarios. Guarda en memoria lo leído durante la petición (las tablas son
/// pequeñas) y se invalida cuando un administrador cambia reglas o feriados.
/// </summary>
internal sealed class SlaTermsProvider(ISlaRepository repository, IOptions<HelpdeskOptions> options) : ISlaTermsProvider
{
    private readonly Dictionary<Guid, ISlaCalendar> _calendars = [];
    private IReadOnlyList<SlaPolicy>? _policies;

    public async Task<SlaTerms> ForPolicyAsync(Guid policyId, CancellationToken cancellationToken)
    {
        var policy = (await PoliciesAsync(cancellationToken)).FirstOrDefault(p => p.Id == policyId)
            ?? throw new InvalidOperationException($"La regla de SLA {policyId} no existe.");
        return SlaTerms.From(policy, await CalendarAsync(policy.CalendarId, cancellationToken));
    }

    public async Task<SlaTerms> SelectAsync(Priority priority, ServiceTier tier, CancellationToken cancellationToken)
    {
        var policy = SlaPolicySelector.Select(await PoliciesAsync(cancellationToken), priority, tier);
        return SlaTerms.From(policy, await CalendarAsync(policy.CalendarId, cancellationToken));
    }

    public async Task<string> PolicyNameAsync(Guid policyId, CancellationToken cancellationToken) =>
        (await PoliciesAsync(cancellationToken)).FirstOrDefault(p => p.Id == policyId)?.Name ?? string.Empty;

    public async Task<DateTimeOffset> AutoCloseAtAsync(DateTimeOffset resolvedAt, CancellationToken cancellationToken)
    {
        var office = await repository.GetCalendarByCodeAsync(options.Value.OfficeCalendarCode, cancellationToken)
            ?? throw new InvalidOperationException($"No existe el calendario '{options.Value.OfficeCalendarCode}'.");
        var calendar = await CalendarAsync(office.Id, cancellationToken);
        return calendar.AddBusinessMinutes(resolvedAt, options.Value.AutoCloseBusinessDays * options.Value.BusinessDayMinutes);
    }

    public void Invalidate()
    {
        _policies = null;
        _calendars.Clear();
    }

    private async Task<IReadOnlyList<SlaPolicy>> PoliciesAsync(CancellationToken cancellationToken) =>
        _policies ??= await repository.ListPoliciesAsync(cancellationToken);

    private async Task<ISlaCalendar> CalendarAsync(Guid calendarId, CancellationToken cancellationToken)
    {
        if (_calendars.TryGetValue(calendarId, out var cached))
        {
            return cached;
        }

        var calendar = await repository.GetCalendarAsync(calendarId, cancellationToken)
            ?? throw new InvalidOperationException($"El calendario {calendarId} no existe.");
        var working = calendar.ToWorkingCalendar();
        _calendars[calendarId] = working;
        return working;
    }
}
