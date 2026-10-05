using Helpdesk.Domain.Catalog;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Persistence.Seed;

/// <summary>
/// Catálogos estables que viajan en las migraciones (datos gestionados por el modelo de EF Core):
/// roles, colas y categorías alineadas con P27, calendarios, feriados 2026 y reglas de SLA (docs/modelo-datos.md).
/// Los identificadores son fijos para que las migraciones sean deterministas.
/// </summary>
public static class ReferenceData
{
    public const string OfficeCalendarCode = "ec-office";
    public const string TwentyFourSevenCalendarCode = "24x7";
    public const string ServiceDeskTeamCode = "service_desk";
    public const string GeneralCategoryCode = "general";

    public static readonly Guid AdminRoleId = Guid.Parse("01920000-0000-7000-8000-000000000001");
    public static readonly Guid SupervisorRoleId = Guid.Parse("01920000-0000-7000-8000-000000000002");
    public static readonly Guid AgentRoleId = Guid.Parse("01920000-0000-7000-8000-000000000003");
    public static readonly Guid CustomerRoleId = Guid.Parse("01920000-0000-7000-8000-000000000004");

    public static readonly Guid OfficeCalendarId = Guid.Parse("01920000-0000-7000-8000-000000000101");
    public static readonly Guid TwentyFourSevenCalendarId = Guid.Parse("01920000-0000-7000-8000-000000000102");

    public static readonly Guid ServiceDeskTeamId = Guid.Parse("01920000-0000-7000-8000-000000000201");
    public static readonly Guid CardOperationsTeamId = Guid.Parse("01920000-0000-7000-8000-000000000202");
    public static readonly Guid DigitalSupportTeamId = Guid.Parse("01920000-0000-7000-8000-000000000203");
    public static readonly Guid PaymentsTransfersTeamId = Guid.Parse("01920000-0000-7000-8000-000000000204");
    public static readonly Guid FraudSecurityTeamId = Guid.Parse("01920000-0000-7000-8000-000000000205");
    public static readonly Guid KycComplianceTeamId = Guid.Parse("01920000-0000-7000-8000-000000000206");
    public static readonly Guid CustomerCareTeamId = Guid.Parse("01920000-0000-7000-8000-000000000207");

    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<(Guid Id, string Code, string Name, string Description)> Teams { get; } =
    [
        (ServiceDeskTeamId, ServiceDeskTeamCode, "Mesa de Servicio N1", "Primer nivel: registro, clasificación y consultas frecuentes"),
        (CardOperationsTeamId, "card_operations", "Operaciones de Tarjetas", "Emisión, entrega, bloqueo de PIN y fallos de tarjetas"),
        (DigitalSupportTeamId, "digital_support", "Soporte Digital", "App, billeteras móviles, tarjetas virtuales y accesos"),
        (PaymentsTransfersTeamId, "payments_transfers", "Pagos y Transferencias", "Transferencias, recargas, pagos y efectivo"),
        (FraudSecurityTeamId, "fraud_security", "Fraude y Seguridad", "Movimientos no reconocidos, tarjetas y dispositivos comprometidos"),
        (KycComplianceTeamId, "kyc_compliance", "Cumplimiento (KYC)", "Verificación de identidad y origen de fondos"),
        (CustomerCareTeamId, "customer_care", "Atención al Cliente", "Consultas informativas, comisiones, divisas y gestión de la cuenta"),
    ];

    public static IReadOnlyList<(Guid Id, string Code, string Name, string Description, Guid DefaultTeamId)> Categories { get; } =
    [
        (Guid.Parse("01920000-0000-7000-8000-000000000301"), "cards", "Tarjetas", "Emisión, entrega, activación, PIN y funcionamiento de tarjetas físicas", CardOperationsTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000302"), "digital_channels", "Banca digital", "App, billeteras móviles, tarjetas virtuales y código de acceso", DigitalSupportTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000303"), "card_payments", "Pagos con tarjeta", "Pagos rechazados, pendientes, revertidos, duplicados y reembolsos", PaymentsTransfersTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000304"), "cash_atm", "Efectivo y cajeros", "Retiros, depósitos y cajeros automáticos", PaymentsTransfersTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000305"), "transfers", "Transferencias", "Envío y recepción de transferencias y beneficiarios", PaymentsTransfersTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000306"), "top_ups", "Recargas", "Recargas de saldo por tarjeta, transferencia o efectivo", PaymentsTransfersTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000307"), "fees_fx", "Comisiones y divisas", "Comisiones cobradas, tipo de cambio y monedas admitidas", CustomerCareTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000308"), "security_fraud", "Seguridad y fraude", "Movimientos no reconocidos, tarjetas o teléfonos perdidos o comprometidos", FraudSecurityTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000309"), "account_kyc", "Cuenta e identidad", "Datos personales, verificación de identidad, requisitos y cierre de cuenta", KycComplianceTeamId),
        (Guid.Parse("01920000-0000-7000-8000-000000000310"), GeneralCategoryCode, "Consultas generales", "Consultas que no encajan en otra categoría", ServiceDeskTeamId),
    ];

    /// <summary>Feriados nacionales 2026 (docs/investigacion.md #28–#30).</summary>
    public static IReadOnlyList<(DateOnly Date, string Name, string LegalBasis)> Holidays2026 { get; } =
    [
        (new DateOnly(2026, 1, 1), "Año Nuevo", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 1, 2), "Descanso por Año Nuevo", "Decreto Ejecutivo 249 (17-dic-2025)"),
        (new DateOnly(2026, 2, 16), "Carnaval (lunes)", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 2, 17), "Carnaval (martes)", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 4, 3), "Viernes Santo", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 5, 1), "Día del Trabajo", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 5, 25), "Batalla de Pichincha (traslado del domingo 24)", "Código del Trabajo art. 65 reformado (R.O. 906)"),
        (new DateOnly(2026, 8, 10), "Primer Grito de Independencia", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 10, 9), "Independencia de Guayaquil", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 11, 2), "Día de los Difuntos", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 11, 3), "Independencia de Cuenca", "Código del Trabajo art. 65"),
        (new DateOnly(2026, 12, 25), "Navidad", "Código del Trabajo art. 65"),
    ];

    /// <summary>Objetivos de SLA en minutos (supuesto S1, configurables por API).</summary>
    public static IReadOnlyList<(Guid Id, string Name, Priority Priority, ServiceTier? Tier, int Response, int Resolution, Guid CalendarId)> SlaPolicies { get; } =
    [
        (Guid.Parse("01920000-0000-7000-8000-000000000401"), "P1 Crítica", Priority.Critical, null, 30, 240, TwentyFourSevenCalendarId),
        (Guid.Parse("01920000-0000-7000-8000-000000000402"), "P2 Alta", Priority.High, null, 60, 480, OfficeCalendarId),
        (Guid.Parse("01920000-0000-7000-8000-000000000403"), "P3 Media", Priority.Medium, null, 240, 1440, OfficeCalendarId),
        (Guid.Parse("01920000-0000-7000-8000-000000000404"), "P4 Baja", Priority.Low, null, 480, 2400, OfficeCalendarId),
        (Guid.Parse("01920000-0000-7000-8000-000000000405"), "P5 Planificación", Priority.Planning, null, 960, 4800, OfficeCalendarId),
        (Guid.Parse("01920000-0000-7000-8000-000000000411"), "P1 Crítica premium", Priority.Critical, ServiceTier.Premium, 15, 120, TwentyFourSevenCalendarId),
        (Guid.Parse("01920000-0000-7000-8000-000000000412"), "P2 Alta premium", Priority.High, ServiceTier.Premium, 30, 240, OfficeCalendarId),
        (Guid.Parse("01920000-0000-7000-8000-000000000413"), "P3 Media premium", Priority.Medium, ServiceTier.Premium, 120, 960, OfficeCalendarId),
    ];

    public static void Seed(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Entity<AppRole>().HasData(
            Role(AdminRoleId, RoleNames.Admin),
            Role(SupervisorRoleId, RoleNames.Supervisor),
            Role(AgentRoleId, RoleNames.Agent),
            Role(CustomerRoleId, RoleNames.Customer));

        builder.Entity<Team>().HasData(Teams.Select(t => new { t.Id, t.Code, t.Name, t.Description, IsActive = true, CreatedAt = SeededAt }));
        builder.Entity<Category>().HasData(Categories.Select(c => new { c.Id, c.Code, c.Name, c.Description, c.DefaultTeamId, IsActive = true }));

        builder.Entity<BusinessCalendar>().HasData(
            new { Id = OfficeCalendarId, Code = OfficeCalendarCode, Name = "Oficina Ecuador (L–V 08:00–17:00)", TimeZoneId = "America/Guayaquil", Is24x7 = false },
            new { Id = TwentyFourSevenCalendarId, Code = TwentyFourSevenCalendarCode, Name = "24×7", TimeZoneId = "America/Guayaquil", Is24x7 = true });

        builder.Entity<BusinessHours>().HasData(OfficeHours());
        builder.Entity<Holiday>().HasData(Holidays2026.Select((h, i) => new
        {
            Id = Guid.Parse($"01920000-0000-7000-8000-0000000006{i + 1:D2}"),
            CalendarId = OfficeCalendarId,
            h.Date,
            h.Name,
            h.LegalBasis,
        }));

        builder.Entity<SlaPolicy>().HasData(SlaPolicies.Select(p => new
        {
            p.Id,
            p.Name,
            p.Priority,
            ServiceTier = p.Tier,
            FirstResponseMinutes = p.Response,
            ResolutionMinutes = p.Resolution,
            WarningPercent = 75,
            p.CalendarId,
            IsActive = true,
        }));
    }

    private static AppRole Role(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = id.ToString(),
    };

    private static IEnumerable<object> OfficeHours()
    {
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var index = 0;
        foreach (var day in weekdays)
        {
            foreach (var (start, end) in new[] { (new TimeOnly(8, 0), new TimeOnly(13, 0)), (new TimeOnly(14, 0), new TimeOnly(17, 0)) })
            {
                index++;
                yield return new
                {
                    Id = Guid.Parse($"01920000-0000-7000-8000-0000000005{index:D2}"),
                    CalendarId = OfficeCalendarId,
                    DayOfWeek = day,
                    StartTime = start,
                    EndTime = end,
                };
            }
        }
    }
}
