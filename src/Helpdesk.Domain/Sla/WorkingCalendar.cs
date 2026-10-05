namespace Helpdesk.Domain.Sla;

/// <summary>
/// Calendario laboral inmutable: horarios por día de la semana en una zona IANA, menos los feriados.
/// Un calendario 24×7 cuenta todo el tiempo de reloj.
/// </summary>
public sealed class WorkingCalendar : ISlaCalendar
{
    /// <summary>Tope de días a recorrer: evita bucles infinitos si el calendario quedara sin horas hábiles.</summary>
    private const int MaxDaysToScan = 3_660;

    private readonly TimeZoneInfo _timeZone;
    private readonly bool _is24x7;
    private readonly Dictionary<DayOfWeek, BusinessInterval[]> _intervals;
    private readonly HashSet<DateOnly> _holidays;

    private WorkingCalendar(TimeZoneInfo timeZone, bool is24x7, Dictionary<DayOfWeek, BusinessInterval[]> intervals, HashSet<DateOnly> holidays)
    {
        _timeZone = timeZone;
        _is24x7 = is24x7;
        _intervals = intervals;
        _holidays = holidays;
    }

    public static WorkingCalendar TwentyFourSeven(TimeZoneInfo timeZone) => new(timeZone, true, [], []);

    public static WorkingCalendar Create(
        TimeZoneInfo timeZone,
        IEnumerable<(DayOfWeek Day, TimeOnly Start, TimeOnly End)> hours,
        IEnumerable<DateOnly> holidays)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(hours);
        ArgumentNullException.ThrowIfNull(holidays);

        var byDay = new Dictionary<DayOfWeek, BusinessInterval[]>();
        foreach (var group in hours.GroupBy(h => h.Day))
        {
            var ordered = group.Select(h => new BusinessInterval(h.Start, h.End)).OrderBy(i => i.Start).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                if (ordered[i].End <= ordered[i].Start)
                {
                    throw new ArgumentException($"Intervalo inválido el {group.Key}: el fin debe ser posterior al inicio.", nameof(hours));
                }

                if (i > 0 && ordered[i].Start < ordered[i - 1].End)
                {
                    throw new ArgumentException($"Intervalos superpuestos el {group.Key}.", nameof(hours));
                }
            }

            byDay[group.Key] = ordered;
        }

        if (byDay.Count == 0)
        {
            throw new ArgumentException("El calendario necesita al menos un intervalo hábil en la semana.", nameof(hours));
        }

        return new WorkingCalendar(timeZone, false, byDay, [.. holidays]);
    }

    public DateTimeOffset AddBusinessMinutes(DateTimeOffset start, int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        if (minutes == 0)
        {
            return start;
        }

        if (_is24x7)
        {
            return start.ToUniversalTime().AddMinutes(minutes);
        }

        var remaining = TimeSpan.FromMinutes(minutes);
        var cursor = ToLocal(start);
        var day = DateOnly.FromDateTime(cursor);

        for (var scanned = 0; scanned < MaxDaysToScan; scanned++, day = day.AddDays(1))
        {
            foreach (var interval in IntervalsFor(day))
            {
                var intervalStart = day.ToDateTime(interval.Start);
                var intervalEnd = day.ToDateTime(interval.End);
                if (cursor >= intervalEnd)
                {
                    continue;
                }

                var from = cursor > intervalStart ? cursor : intervalStart;
                var available = intervalEnd - from;
                if (remaining <= available)
                {
                    return ToUtc(from + remaining);
                }

                remaining -= available;
                cursor = intervalEnd;
            }
        }

        throw new InvalidOperationException("El calendario no tiene horas hábiles suficientes en el rango analizado.");
    }

    public int BusinessMinutesBetween(DateTimeOffset start, DateTimeOffset finish)
    {
        if (finish <= start)
        {
            return 0;
        }

        if (_is24x7)
        {
            return (int)Math.Floor((finish - start).TotalMinutes);
        }

        var localFrom = ToLocal(start);
        var localTo = ToLocal(finish);
        var total = TimeSpan.Zero;
        for (var day = DateOnly.FromDateTime(localFrom); day <= DateOnly.FromDateTime(localTo); day = day.AddDays(1))
        {
            foreach (var interval in IntervalsFor(day))
            {
                var overlapStart = Max(day.ToDateTime(interval.Start), localFrom);
                var overlapEnd = Min(day.ToDateTime(interval.End), localTo);
                if (overlapEnd > overlapStart)
                {
                    total += overlapEnd - overlapStart;
                }
            }
        }

        return (int)Math.Floor(total.TotalMinutes);
    }

    /// <summary>¿El instante cae dentro de horario hábil?</summary>
    public bool IsBusinessTime(DateTimeOffset instant)
    {
        if (_is24x7)
        {
            return true;
        }

        var local = ToLocal(instant);
        var time = TimeOnly.FromDateTime(local);
        return IntervalsFor(DateOnly.FromDateTime(local)).Any(i => time >= i.Start && time < i.End);
    }

    private BusinessInterval[] IntervalsFor(DateOnly day) =>
        _holidays.Contains(day) || !_intervals.TryGetValue(day.DayOfWeek, out var intervals) ? [] : intervals;

    private DateTime ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime;

    private DateTimeOffset ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (_timeZone.IsInvalidTime(unspecified))
        {
            // Hora inexistente por cambio a horario de verano: se avanza al primer instante válido.
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, _timeZone), TimeSpan.Zero);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private readonly record struct BusinessInterval(TimeOnly Start, TimeOnly End);
}
