using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Helpdesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "business_calendars",
                columns: new[] { "id", "code", "is_24x7", "name", "time_zone" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000101"), "ec-office", false, "Oficina Ecuador (L–V 08:00–17:00)", "America/Guayaquil" },
                    { new Guid("01920000-0000-7000-8000-000000000102"), "24x7", true, "24×7", "America/Guayaquil" }
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "concurrency_stamp", "name", "normalized_name" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000001"), "01920000-0000-7000-8000-000000000001", "Admin", "ADMIN" },
                    { new Guid("01920000-0000-7000-8000-000000000002"), "01920000-0000-7000-8000-000000000002", "Supervisor", "SUPERVISOR" },
                    { new Guid("01920000-0000-7000-8000-000000000003"), "01920000-0000-7000-8000-000000000003", "Agent", "AGENT" },
                    { new Guid("01920000-0000-7000-8000-000000000004"), "01920000-0000-7000-8000-000000000004", "Customer", "CUSTOMER" }
                });

            migrationBuilder.InsertData(
                table: "teams",
                columns: new[] { "id", "code", "created_at", "description", "is_active", "name" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000201"), "service_desk", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Primer nivel: registro, clasificación y consultas frecuentes", true, "Mesa de Servicio N1" },
                    { new Guid("01920000-0000-7000-8000-000000000202"), "card_operations", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Emisión, entrega, bloqueo de PIN y fallos de tarjetas", true, "Operaciones de Tarjetas" },
                    { new Guid("01920000-0000-7000-8000-000000000203"), "digital_support", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "App, billeteras móviles, tarjetas virtuales y accesos", true, "Soporte Digital" },
                    { new Guid("01920000-0000-7000-8000-000000000204"), "payments_transfers", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Transferencias, recargas, pagos y efectivo", true, "Pagos y Transferencias" },
                    { new Guid("01920000-0000-7000-8000-000000000205"), "fraud_security", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Movimientos no reconocidos, tarjetas y dispositivos comprometidos", true, "Fraude y Seguridad" },
                    { new Guid("01920000-0000-7000-8000-000000000206"), "kyc_compliance", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Verificación de identidad y origen de fondos", true, "Cumplimiento (KYC)" },
                    { new Guid("01920000-0000-7000-8000-000000000207"), "customer_care", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Consultas informativas, comisiones, divisas y gestión de la cuenta", true, "Atención al Cliente" }
                });

            migrationBuilder.InsertData(
                table: "business_hours",
                columns: new[] { "id", "calendar_id", "day_of_week", "end_time", "start_time" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000501"), new Guid("01920000-0000-7000-8000-000000000101"), (short)1, new TimeOnly(13, 0, 0), new TimeOnly(8, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000502"), new Guid("01920000-0000-7000-8000-000000000101"), (short)1, new TimeOnly(17, 0, 0), new TimeOnly(14, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000503"), new Guid("01920000-0000-7000-8000-000000000101"), (short)2, new TimeOnly(13, 0, 0), new TimeOnly(8, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000504"), new Guid("01920000-0000-7000-8000-000000000101"), (short)2, new TimeOnly(17, 0, 0), new TimeOnly(14, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000505"), new Guid("01920000-0000-7000-8000-000000000101"), (short)3, new TimeOnly(13, 0, 0), new TimeOnly(8, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000506"), new Guid("01920000-0000-7000-8000-000000000101"), (short)3, new TimeOnly(17, 0, 0), new TimeOnly(14, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000507"), new Guid("01920000-0000-7000-8000-000000000101"), (short)4, new TimeOnly(13, 0, 0), new TimeOnly(8, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000508"), new Guid("01920000-0000-7000-8000-000000000101"), (short)4, new TimeOnly(17, 0, 0), new TimeOnly(14, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000509"), new Guid("01920000-0000-7000-8000-000000000101"), (short)5, new TimeOnly(13, 0, 0), new TimeOnly(8, 0, 0) },
                    { new Guid("01920000-0000-7000-8000-000000000510"), new Guid("01920000-0000-7000-8000-000000000101"), (short)5, new TimeOnly(17, 0, 0), new TimeOnly(14, 0, 0) }
                });

            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "id", "code", "default_team_id", "description", "is_active", "name" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000301"), "cards", new Guid("01920000-0000-7000-8000-000000000202"), "Emisión, entrega, activación, PIN y funcionamiento de tarjetas físicas", true, "Tarjetas" },
                    { new Guid("01920000-0000-7000-8000-000000000302"), "digital_channels", new Guid("01920000-0000-7000-8000-000000000203"), "App, billeteras móviles, tarjetas virtuales y código de acceso", true, "Banca digital" },
                    { new Guid("01920000-0000-7000-8000-000000000303"), "card_payments", new Guid("01920000-0000-7000-8000-000000000204"), "Pagos rechazados, pendientes, revertidos, duplicados y reembolsos", true, "Pagos con tarjeta" },
                    { new Guid("01920000-0000-7000-8000-000000000304"), "cash_atm", new Guid("01920000-0000-7000-8000-000000000204"), "Retiros, depósitos y cajeros automáticos", true, "Efectivo y cajeros" },
                    { new Guid("01920000-0000-7000-8000-000000000305"), "transfers", new Guid("01920000-0000-7000-8000-000000000204"), "Envío y recepción de transferencias y beneficiarios", true, "Transferencias" },
                    { new Guid("01920000-0000-7000-8000-000000000306"), "top_ups", new Guid("01920000-0000-7000-8000-000000000204"), "Recargas de saldo por tarjeta, transferencia o efectivo", true, "Recargas" },
                    { new Guid("01920000-0000-7000-8000-000000000307"), "fees_fx", new Guid("01920000-0000-7000-8000-000000000207"), "Comisiones cobradas, tipo de cambio y monedas admitidas", true, "Comisiones y divisas" },
                    { new Guid("01920000-0000-7000-8000-000000000308"), "security_fraud", new Guid("01920000-0000-7000-8000-000000000205"), "Movimientos no reconocidos, tarjetas o teléfonos perdidos o comprometidos", true, "Seguridad y fraude" },
                    { new Guid("01920000-0000-7000-8000-000000000309"), "account_kyc", new Guid("01920000-0000-7000-8000-000000000206"), "Datos personales, verificación de identidad, requisitos y cierre de cuenta", true, "Cuenta e identidad" },
                    { new Guid("01920000-0000-7000-8000-000000000310"), "general", new Guid("01920000-0000-7000-8000-000000000201"), "Consultas que no encajan en otra categoría", true, "Consultas generales" }
                });

            migrationBuilder.InsertData(
                table: "holidays",
                columns: new[] { "id", "calendar_id", "date", "legal_basis", "name" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000601"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 1, 1), "Código del Trabajo art. 65", "Año Nuevo" },
                    { new Guid("01920000-0000-7000-8000-000000000602"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 1, 2), "Decreto Ejecutivo 249 (17-dic-2025)", "Descanso por Año Nuevo" },
                    { new Guid("01920000-0000-7000-8000-000000000603"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 2, 16), "Código del Trabajo art. 65", "Carnaval (lunes)" },
                    { new Guid("01920000-0000-7000-8000-000000000604"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 2, 17), "Código del Trabajo art. 65", "Carnaval (martes)" },
                    { new Guid("01920000-0000-7000-8000-000000000605"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 4, 3), "Código del Trabajo art. 65", "Viernes Santo" },
                    { new Guid("01920000-0000-7000-8000-000000000606"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 5, 1), "Código del Trabajo art. 65", "Día del Trabajo" },
                    { new Guid("01920000-0000-7000-8000-000000000607"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 5, 25), "Código del Trabajo art. 65 reformado (R.O. 906)", "Batalla de Pichincha (traslado del domingo 24)" },
                    { new Guid("01920000-0000-7000-8000-000000000608"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 8, 10), "Código del Trabajo art. 65", "Primer Grito de Independencia" },
                    { new Guid("01920000-0000-7000-8000-000000000609"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 10, 9), "Código del Trabajo art. 65", "Independencia de Guayaquil" },
                    { new Guid("01920000-0000-7000-8000-000000000610"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 11, 2), "Código del Trabajo art. 65", "Día de los Difuntos" },
                    { new Guid("01920000-0000-7000-8000-000000000611"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 11, 3), "Código del Trabajo art. 65", "Independencia de Cuenca" },
                    { new Guid("01920000-0000-7000-8000-000000000612"), new Guid("01920000-0000-7000-8000-000000000101"), new DateOnly(2026, 12, 25), "Código del Trabajo art. 65", "Navidad" }
                });

            migrationBuilder.InsertData(
                table: "sla_policies",
                columns: new[] { "id", "calendar_id", "first_response_minutes", "is_active", "name", "priority", "resolution_minutes", "service_tier", "warning_percent" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000401"), new Guid("01920000-0000-7000-8000-000000000102"), 30, true, "P1 Crítica", (short)1, 240, null, (short)75 },
                    { new Guid("01920000-0000-7000-8000-000000000402"), new Guid("01920000-0000-7000-8000-000000000101"), 60, true, "P2 Alta", (short)2, 480, null, (short)75 },
                    { new Guid("01920000-0000-7000-8000-000000000403"), new Guid("01920000-0000-7000-8000-000000000101"), 240, true, "P3 Media", (short)3, 1440, null, (short)75 },
                    { new Guid("01920000-0000-7000-8000-000000000404"), new Guid("01920000-0000-7000-8000-000000000101"), 480, true, "P4 Baja", (short)4, 2400, null, (short)75 },
                    { new Guid("01920000-0000-7000-8000-000000000405"), new Guid("01920000-0000-7000-8000-000000000101"), 960, true, "P5 Planificación", (short)5, 4800, null, (short)75 },
                    { new Guid("01920000-0000-7000-8000-000000000411"), new Guid("01920000-0000-7000-8000-000000000102"), 15, true, "P1 Crítica premium", (short)1, 120, "premium", (short)75 },
                    { new Guid("01920000-0000-7000-8000-000000000412"), new Guid("01920000-0000-7000-8000-000000000101"), 30, true, "P2 Alta premium", (short)2, 240, "premium", (short)75 },
                    { new Guid("01920000-0000-7000-8000-000000000413"), new Guid("01920000-0000-7000-8000-000000000101"), 120, true, "P3 Media premium", (short)3, 960, "premium", (short)75 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000501"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000502"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000503"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000504"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000505"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000506"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000507"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000508"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000509"));

            migrationBuilder.DeleteData(
                table: "business_hours",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000510"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000301"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000302"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000303"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000304"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000305"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000306"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000307"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000308"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000309"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000310"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000601"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000602"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000603"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000604"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000605"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000606"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000607"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000608"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000609"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000610"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000611"));

            migrationBuilder.DeleteData(
                table: "holidays",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000612"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000003"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000004"));

            migrationBuilder.DeleteData(
                table: "sla_policies",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000401"));

            migrationBuilder.DeleteData(
                table: "sla_policies",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000402"));

            migrationBuilder.DeleteData(
                table: "sla_policies",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000403"));

            migrationBuilder.DeleteData(
                table: "sla_policies",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000404"));

            migrationBuilder.DeleteData(
                table: "sla_policies",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000405"));

            migrationBuilder.DeleteData(
                table: "sla_policies",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000411"));

            migrationBuilder.DeleteData(
                table: "sla_policies",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000412"));

            migrationBuilder.DeleteData(
                table: "sla_policies",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000413"));

            migrationBuilder.DeleteData(
                table: "business_calendars",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000101"));

            migrationBuilder.DeleteData(
                table: "business_calendars",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000102"));

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000201"));

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000202"));

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000203"));

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000204"));

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000205"));

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000206"));

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000207"));
        }
    }
}
