using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Helpdesk.Infrastructure.Persistence.Converters;

/// <summary>Guarda enumeraciones como texto snake_case (<c>InProgress</c> → <c>in_progress</c>), legible en SQL.</summary>
internal sealed class SnakeCaseEnumConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToDb = Enum.GetValues<TEnum>().ToDictionary(v => v, v => ToSnake(v.ToString()));
    private static readonly Dictionary<string, TEnum> FromDb = ToDb.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    public SnakeCaseEnumConverter()
        : base(v => ToDb[v], v => FromDb[v])
    {
    }

    public static IReadOnlyCollection<string> DatabaseValues => ToDb.Values;

    /// <summary>Lista para un CHECK constraint: <c>'new', 'assigned', …</c>.</summary>
    public static string SqlList => string.Join(", ", ToDb.Values.Select(v => $"'{v}'"));

    public static string ToDatabase(TEnum value) => ToDb[value];

    private static string ToSnake(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}

/// <summary>Días de la semana en numeración ISO (1 = lunes … 7 = domingo).</summary>
internal sealed class IsoDayOfWeekConverter : ValueConverter<DayOfWeek, short>
{
    public IsoDayOfWeekConverter()
        : base(v => v == DayOfWeek.Sunday ? (short)7 : (short)v, v => v == 7 ? DayOfWeek.Sunday : (DayOfWeek)v)
    {
    }
}

/// <summary>PostgreSQL <c>timestamptz</c> exige offset 0: se normaliza a UTC antes de escribir.</summary>
internal sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(v => v.ToUniversalTime(), v => v)
    {
    }
}
