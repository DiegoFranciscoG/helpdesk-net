namespace Helpdesk.Domain.Sla;

/// <summary>Aritmética de tiempo hábil sobre un calendario laboral (R3, R4).</summary>
public interface ISlaCalendar
{
    /// <summary>Instante que resulta de sumar <paramref name="minutes"/> minutos hábiles a <paramref name="start"/>.</summary>
    DateTimeOffset AddBusinessMinutes(DateTimeOffset start, int minutes);

    /// <summary>Minutos hábiles completos entre dos instantes (0 si <paramref name="finish"/> no es posterior).</summary>
    int BusinessMinutesBetween(DateTimeOffset start, DateTimeOffset finish);
}
