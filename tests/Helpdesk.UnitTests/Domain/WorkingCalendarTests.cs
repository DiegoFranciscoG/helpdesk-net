using Helpdesk.Domain.Sla;
using static Helpdesk.UnitTests.TestData;

namespace Helpdesk.UnitTests.Domain;

/// <summary>Cálculo de tiempo hábil con horario de oficina, almuerzo, fines de semana y feriados de Ecuador 2026.</summary>
public sealed class WorkingCalendarTests
{
    private readonly WorkingCalendar _calendar = EcuadorOffice();

    [Fact]
    public void Adds_minutes_inside_the_same_interval() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 5, 9, 0), 45).Should().Be(Local(2026, 10, 5, 9, 45));

    [Fact]
    public void Skips_the_lunch_break() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 5, 12, 30), 60).Should().Be(Local(2026, 10, 5, 14, 30));

    [Fact]
    public void Continues_on_the_next_business_day() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 5, 16, 0), 120).Should().Be(Local(2026, 10, 6, 9, 0));

    [Fact]
    public void A_ticket_created_before_opening_starts_counting_at_eight() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 5, 6, 15), 30).Should().Be(Local(2026, 10, 5, 8, 30));

    [Fact]
    public void A_ticket_created_on_saturday_starts_counting_on_monday() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 10, 11, 0), 60).Should().Be(Local(2026, 10, 12, 9, 0));

    [Fact]
    public void Friday_afternoon_rolls_over_the_weekend() =>
        _calendar.AddBusinessMinutes(Local(2026, 9, 25, 16, 0), 120).Should().Be(Local(2026, 9, 28, 9, 0));

    [Fact]
    public void Carnival_monday_and_tuesday_are_not_business_days() =>
        _calendar.AddBusinessMinutes(Local(2026, 2, 13, 16, 0), 120).Should().Be(Local(2026, 2, 18, 9, 0));

    [Fact]
    public void New_year_and_the_decree_249_bridge_day_are_skipped() =>
        _calendar.AddBusinessMinutes(Local(2025, 12, 31, 16, 30), 60).Should().Be(Local(2026, 1, 5, 8, 30));

    [Fact]
    public void Battle_of_pichincha_is_observed_on_monday_may_25() =>
        _calendar.AddBusinessMinutes(Local(2026, 5, 22, 16, 0), 120).Should().Be(Local(2026, 5, 26, 9, 0));

    [Fact]
    public void All_souls_day_and_cuenca_independence_make_a_four_day_weekend() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 30, 16, 0), 240).Should().Be(Local(2026, 11, 4, 11, 0));

    [Fact]
    public void Guayaquil_independence_friday_is_skipped() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 8, 16, 0), 90).Should().Be(Local(2026, 10, 12, 8, 30));

    [Fact]
    public void The_same_dates_without_holidays_would_end_earlier() =>
        EcuadorOfficeWithoutHolidays().AddBusinessMinutes(Local(2026, 2, 13, 16, 0), 120).Should().Be(Local(2026, 2, 16, 9, 0));

    [Fact]
    public void Ending_exactly_at_closing_time_returns_the_closing_instant() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 5, 10, 0), 360).Should().Be(Local(2026, 10, 5, 17, 0));

    [Fact]
    public void Zero_minutes_returns_the_same_instant() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 10, 3, 0), 0).Should().Be(Local(2026, 10, 10, 3, 0));

    [Fact]
    public void Negative_minutes_are_rejected()
    {
        var act = () => _calendar.AddBusinessMinutes(Local(2026, 10, 5, 9, 0), -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Business_minutes_between_two_instants_on_the_same_day_exclude_lunch() =>
        _calendar.BusinessMinutesBetween(Local(2026, 10, 5, 12, 0), Local(2026, 10, 5, 15, 0)).Should().Be(120);

    [Fact]
    public void Business_minutes_between_exclude_weekends_and_holidays() =>
        // Viernes 30-oct 16:00 → miércoles 4-nov 09:00: 60 min del viernes + 60 min del miércoles.
        _calendar.BusinessMinutesBetween(Local(2026, 10, 30, 16, 0), Local(2026, 11, 4, 9, 0)).Should().Be(120);

    [Fact]
    public void Business_minutes_between_is_zero_when_end_is_not_after_start()
    {
        _calendar.BusinessMinutesBetween(Local(2026, 10, 5, 15, 0), Local(2026, 10, 5, 12, 0)).Should().Be(0);
        _calendar.BusinessMinutesBetween(Local(2026, 10, 5, 15, 0), Local(2026, 10, 5, 15, 0)).Should().Be(0);
    }

    [Fact]
    public void A_whole_holiday_contributes_nothing() =>
        _calendar.BusinessMinutesBetween(Local(2026, 8, 10, 0, 0), Local(2026, 8, 10, 23, 59)).Should().Be(0);

    [Fact]
    public void Adding_then_measuring_returns_the_same_minutes_for_random_inputs()
    {
        var random = new Random(2026);
        for (var i = 0; i < 500; i++)
        {
            var start = Local(2026, 1, 1, 0, 0).AddMinutes(random.Next(0, 360 * 24 * 60)).AddSeconds(random.Next(0, 60));
            var minutes = random.Next(1, 20 * 480);

            var end = _calendar.AddBusinessMinutes(start, minutes);

            _calendar.BusinessMinutesBetween(start, end).Should().Be(minutes, $"inicio {start:O} + {minutes} min");
            end.Offset.Should().Be(TimeSpan.Zero);
        }
    }

    [Fact]
    public void Results_are_returned_in_utc() =>
        _calendar.AddBusinessMinutes(Local(2026, 10, 5, 9, 0), 60).Should().Be(new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Twenty_four_seven_counts_wall_clock_time_including_holidays()
    {
        var calendar = TwentyFourSeven();

        calendar.AddBusinessMinutes(Local(2026, 12, 24, 23, 0), 120).Should().Be(Local(2026, 12, 25, 1, 0));
        calendar.BusinessMinutesBetween(Local(2026, 12, 24, 23, 0), Local(2026, 12, 25, 1, 0)).Should().Be(120);
        calendar.IsBusinessTime(Local(2026, 12, 25, 3, 0)).Should().BeTrue();
    }

    [Theory]
    [InlineData(2026, 10, 5, 9, 0, true)]
    [InlineData(2026, 10, 5, 13, 30, false)]
    [InlineData(2026, 10, 5, 17, 0, false)]
    [InlineData(2026, 10, 10, 10, 0, false)]
    [InlineData(2026, 11, 2, 10, 0, false)]
    public void Detects_business_time(int year, int month, int day, int hour, int minute, bool expected) =>
        _calendar.IsBusinessTime(Local(year, month, day, hour, minute)).Should().Be(expected);

    [Fact]
    public void Rejects_overlapping_intervals()
    {
        var act = () => WorkingCalendar.Create(
            Guayaquil,
            [(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(13, 0)), (DayOfWeek.Monday, new TimeOnly(12, 0), new TimeOnly(17, 0))],
            []);

        act.Should().Throw<ArgumentException>().WithMessage("*superpuestos*");
    }

    [Fact]
    public void Rejects_intervals_that_end_before_they_start()
    {
        var act = () => WorkingCalendar.Create(Guayaquil, [(DayOfWeek.Monday, new TimeOnly(17, 0), new TimeOnly(8, 0))], []);

        act.Should().Throw<ArgumentException>().WithMessage("*fin*");
    }

    [Fact]
    public void Rejects_a_calendar_without_business_hours()
    {
        var act = () => WorkingCalendar.Create(Guayaquil, [], []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Persisted_calendar_builds_an_equivalent_working_calendar()
    {
        var entity = new BusinessCalendar(Guid.CreateVersion7(), "ec-office", "Oficina Ecuador", "America/Guayaquil", is24x7: false);
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            entity.AddHours(day, new TimeOnly(8, 0), new TimeOnly(13, 0));
            entity.AddHours(day, new TimeOnly(14, 0), new TimeOnly(17, 0));
        }

        entity.AddHoliday(new DateOnly(2026, 2, 16), "Carnaval", "Código del Trabajo art. 65");
        entity.AddHoliday(new DateOnly(2026, 2, 17), "Carnaval", "Código del Trabajo art. 65");

        entity.ToWorkingCalendar().AddBusinessMinutes(Local(2026, 2, 13, 16, 0), 120).Should().Be(Local(2026, 2, 18, 9, 0));
    }

    [Fact]
    public void Persisted_calendar_rejects_duplicate_holidays_and_removes_existing_ones()
    {
        var entity = new BusinessCalendar(Guid.CreateVersion7(), "ec-office", "Oficina", "America/Guayaquil", is24x7: false);
        var holiday = entity.AddHoliday(new DateOnly(2026, 8, 10), "Primer Grito", "Código del Trabajo art. 65");

        var duplicate = () => entity.AddHoliday(new DateOnly(2026, 8, 10), "Repetido", "x");
        duplicate.Should().Throw<Helpdesk.Domain.Common.DomainException>().Which.Code.Should().Be("holiday_exists");

        entity.RemoveHoliday(holiday.Id);
        entity.Holidays.Should().BeEmpty();

        var missing = () => entity.RemoveHoliday(Guid.CreateVersion7());
        missing.Should().Throw<Helpdesk.Domain.Common.DomainException>().Which.Code.Should().Be("holiday_not_found");
    }

    [Fact]
    public void Persisted_twenty_four_seven_calendar_ignores_hours() =>
        new BusinessCalendar(Guid.CreateVersion7(), "24x7", "24x7", "America/Guayaquil", is24x7: true)
            .ToWorkingCalendar()
            .AddBusinessMinutes(Local(2026, 1, 1, 0, 0), 60)
            .Should().Be(Local(2026, 1, 1, 1, 0));
}
