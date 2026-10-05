using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.UnitTests;

/// <summary>Calendario de oficina de Ecuador con los feriados nacionales 2026 (docs/investigacion.md #28–#30).</summary>
internal static class TestData
{
    public static readonly TimeZoneInfo Guayaquil = TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil");

    public static readonly DateOnly[] Holidays2026 =
    [
        new(2026, 1, 1),
        new(2026, 1, 2),
        new(2026, 2, 16),
        new(2026, 2, 17),
        new(2026, 4, 3),
        new(2026, 5, 1),
        new(2026, 5, 25),
        new(2026, 8, 10),
        new(2026, 10, 9),
        new(2026, 11, 2),
        new(2026, 11, 3),
        new(2026, 12, 25),
    ];

    public static WorkingCalendar EcuadorOffice() => WorkingCalendar.Create(Guayaquil, OfficeHours(), Holidays2026);

    public static WorkingCalendar EcuadorOfficeWithoutHolidays() => WorkingCalendar.Create(Guayaquil, OfficeHours(), []);

    public static WorkingCalendar TwentyFourSeven() => WorkingCalendar.TwentyFourSeven(Guayaquil);

    /// <summary>Hora local de Ecuador (UTC−05:00).</summary>
    public static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(-5));

    public static SlaTerms P2Terms(ISlaCalendar? calendar = null) =>
        new(PolicyP2, 60, 480, 75, calendar ?? EcuadorOffice());

    public static SlaTerms P1Terms() => new(PolicyP1, 30, 240, 75, TwentyFourSeven());

    public static readonly Guid PolicyP1 = Guid.Parse("00000000-0000-7000-8000-000000000001");
    public static readonly Guid PolicyP2 = Guid.Parse("00000000-0000-7000-8000-000000000002");
    public static readonly Guid TeamA = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    public static readonly Guid TeamB = Guid.Parse("00000000-0000-7000-8000-0000000000b1");
    public static readonly Guid Customer = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    public static readonly Guid CustomerUser = Guid.Parse("00000000-0000-7000-8000-0000000000c2");
    public static readonly Guid AgentUser = Guid.Parse("00000000-0000-7000-8000-0000000000d1");
    public static readonly Guid OtherAgent = Guid.Parse("00000000-0000-7000-8000-0000000000d2");

    public static NewTicket NewTicket(Impact impact = Impact.Medium, Urgency urgency = Urgency.High) =>
        new("No puedo ingresar a la banca móvil", "Desde ayer la app muestra error al iniciar sesión.", TicketChannel.Portal, impact, urgency, null, TeamA, Customer, CustomerUser);

    private static IEnumerable<(DayOfWeek, TimeOnly, TimeOnly)> OfficeHours()
    {
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            yield return (day, new TimeOnly(8, 0), new TimeOnly(13, 0));
            yield return (day, new TimeOnly(14, 0), new TimeOnly(17, 0));
        }
    }
}
