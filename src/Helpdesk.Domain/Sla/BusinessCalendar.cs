using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Sla;

/// <summary>Calendario persistido (horarios + feriados) del que se construye un <see cref="WorkingCalendar"/>.</summary>
public sealed class BusinessCalendar : Entity
{
    private readonly List<BusinessHours> _hours = [];
    private readonly List<Holiday> _holidays = [];

    private BusinessCalendar()
    {
        Code = string.Empty;
        Name = string.Empty;
        TimeZoneId = string.Empty;
    }

    public BusinessCalendar(Guid id, string code, string name, string timeZoneId, bool is24x7)
    {
        Id = id;
        Code = code;
        Name = name;
        TimeZoneId = timeZoneId;
        Is24x7 = is24x7;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string TimeZoneId { get; private set; }

    public bool Is24x7 { get; private set; }

    public IReadOnlyCollection<BusinessHours> Hours => _hours.AsReadOnly();

    public IReadOnlyCollection<Holiday> Holidays => _holidays.AsReadOnly();

    public void AddHours(DayOfWeek dayOfWeek, TimeOnly start, TimeOnly end) =>
        _hours.Add(new BusinessHours(Guid.CreateVersion7(), Id, dayOfWeek, start, end));

    public Holiday AddHoliday(DateOnly date, string name, string legalBasis)
    {
        if (_holidays.Any(h => h.Date == date))
        {
            throw new DomainException("holiday_exists", $"Ya existe un feriado el {date:yyyy-MM-dd} en este calendario.");
        }

        var holiday = new Holiday(Guid.CreateVersion7(), Id, date, name, legalBasis);
        _holidays.Add(holiday);
        return holiday;
    }

    public void RemoveHoliday(Guid holidayId)
    {
        var holiday = _holidays.FirstOrDefault(h => h.Id == holidayId)
            ?? throw new DomainException("holiday_not_found", "El feriado no existe en este calendario.");
        _holidays.Remove(holiday);
    }

    public ISlaCalendar ToWorkingCalendar()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        return Is24x7
            ? WorkingCalendar.TwentyFourSeven(timeZone)
            : WorkingCalendar.Create(
                timeZone,
                _hours.Select(h => (h.DayOfWeek, h.StartTime, h.EndTime)),
                _holidays.Select(h => h.Date));
    }
}

/// <summary>Intervalo hábil de un día de la semana.</summary>
public sealed class BusinessHours
{
    private BusinessHours()
    {
    }

    public BusinessHours(Guid id, Guid calendarId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        Id = id;
        CalendarId = calendarId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
    }

    public Guid Id { get; private set; }

    public Guid CalendarId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }
}

/// <summary>Día no laborable con su base legal (R4).</summary>
public sealed class Holiday
{
    private Holiday()
    {
        Name = string.Empty;
        LegalBasis = string.Empty;
    }

    public Holiday(Guid id, Guid calendarId, DateOnly date, string name, string legalBasis)
    {
        Id = id;
        CalendarId = calendarId;
        Date = date;
        Name = name;
        LegalBasis = legalBasis;
    }

    public Guid Id { get; private set; }

    public Guid CalendarId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Name { get; private set; }

    public string LegalBasis { get; private set; }
}
