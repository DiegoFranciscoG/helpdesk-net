using Helpdesk.Application.Sla;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Helpdesk.Infrastructure.Persistence.Seed;

/// <summary>
/// Datos de demostración 100 % ficticios (nombres inventados, correos en dominios reservados <c>.example</c>).
/// Simula 60 días de operación pasando por los mismos métodos del dominio, así que los vencimientos, pausas
/// e incumplimientos son coherentes con las reglas de SLA. Solo corre si la base no tiene clientes.
/// </summary>
public sealed partial class DemoDataSeeder(
    HelpdeskDbContext db,
    UserManager<AppUser> userManager,
    ISlaTermsProvider sla,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<DemoDataSeeder> logger)
{
    public const string DemoDomain = "helpdesk.example";
    private const int TicketCount = 120;
    private const int HistoryDays = 60;

    private static readonly (string Name, string Email, string Role, string[] Teams, string[] LeadOf)[] Staff =
    [
        ("Ana Villacís", $"admin@{DemoDomain}", RoleNames.Admin, [], []),
        ("Lucía Paredes", $"supervisor@{DemoDomain}", RoleNames.Supervisor, [], []),
        ("Carlos Vélez", $"agente@{DemoDomain}", RoleNames.Agent, ["service_desk", "digital_support"], ["service_desk"]),
        ("María José Andrade", $"mariajose.andrade@{DemoDomain}", RoleNames.Agent, ["service_desk"], []),
        ("Diana Torres", $"diana.torres@{DemoDomain}", RoleNames.Agent, ["digital_support"], ["digital_support"]),
        ("Jorge Salazar", $"jorge.salazar@{DemoDomain}", RoleNames.Agent, ["payments_transfers"], ["payments_transfers"]),
        ("Paola Cedeño", $"paola.cedeno@{DemoDomain}", RoleNames.Agent, ["payments_transfers", "customer_care"], []),
        ("Andrés Ortiz", $"andres.ortiz@{DemoDomain}", RoleNames.Agent, ["fraud_security"], ["fraud_security"]),
        ("Gabriela Ríos", $"gabriela.rios@{DemoDomain}", RoleNames.Agent, ["card_operations"], ["card_operations"]),
        ("Fernando León", $"fernando.leon@{DemoDomain}", RoleNames.Agent, ["kyc_compliance", "customer_care"], ["kyc_compliance", "customer_care"]),
    ];

    private static readonly (string Name, CustomerKind Kind, ServiceTier Tier, string UserName, string UserEmail)[] DemoCustomers =
    [
        ("Sofía Mendoza", CustomerKind.Person, ServiceTier.Standard, "Sofía Mendoza", $"cliente@{DemoDomain}"),
        ("Textiles Nubeverde S.A.", CustomerKind.Organization, ServiceTier.Premium, "Ricardo Benítez", "ricardo.benitez@nubeverde.example"),
        ("Luis Cárdenas", CustomerKind.Person, ServiceTier.Standard, "Luis Cárdenas", "luis.cardenas@correo.example"),
        ("Ferretería Tres Volcanes", CustomerKind.Organization, ServiceTier.Standard, "Elena Quishpe", "elena.quishpe@tresvolcanes.example"),
        ("Valeria Guzmán", CustomerKind.Person, ServiceTier.Premium, "Valeria Guzmán", "valeria.guzman@correo.example"),
        ("Agroexport Cóndor Azul", CustomerKind.Organization, ServiceTier.Premium, "Martín Pazmiño", "martin.pazmino@condorazul.example"),
        ("Daniela Espinoza", CustomerKind.Person, ServiceTier.Standard, "Daniela Espinoza", "daniela.espinoza@correo.example"),
        ("Pedro Naranjo", CustomerKind.Person, ServiceTier.Standard, "Pedro Naranjo", "pedro.naranjo@correo.example"),
    ];

    private static readonly (string Category, string Title, string Description, Impact Impact, Urgency Urgency)[] Templates =
    [
        ("cards", "Mi tarjeta de débito no llega", "Solicité la reposición de mi tarjeta hace 10 días y todavía no la recibo en mi domicilio.", Impact.Low, Urgency.Medium),
        ("cards", "No puedo activar la tarjeta nueva", "La app me pide los últimos dígitos de la tarjeta y responde que el código es incorrecto.", Impact.Low, Urgency.Medium),
        ("cards", "PIN bloqueado en el cajero", "Ingresé mal el PIN tres veces y ahora la tarjeta aparece bloqueada para retiros.", Impact.Medium, Urgency.Medium),
        ("digital_channels", "Error al iniciar sesión en la app", "Desde la actualización de ayer la app se cierra apenas ingreso mi usuario.", Impact.Medium, Urgency.High),
        ("digital_channels", "No me llega el código de verificación", "Intento registrar un celular nuevo pero el SMS con el código nunca llega.", Impact.Low, Urgency.High),
        ("digital_channels", "La billetera móvil rechaza mi tarjeta", "Quise agregar la tarjeta a la billetera del teléfono y aparece un error genérico.", Impact.Low, Urgency.Low),
        ("card_payments", "Cobro duplicado en un comercio", "En el estado de cuenta aparecen dos consumos iguales del mismo supermercado el mismo día.", Impact.Medium, Urgency.Medium),
        ("card_payments", "Pago rechazado en tienda en línea", "Mi compra fue rechazada aunque tengo saldo disponible y la tarjeta está activa.", Impact.Low, Urgency.Medium),
        ("card_payments", "Reembolso que no se acredita", "El comercio me confirmó la devolución hace una semana y todavía no se refleja en mi cuenta.", Impact.Low, Urgency.Low),
        ("cash_atm", "El cajero no entregó el dinero", "Hice un retiro de 100 dólares, el cajero no dispensó el efectivo pero sí se debitó de mi cuenta.", Impact.Medium, Urgency.High),
        ("cash_atm", "Depósito en cajero no reflejado", "Deposité en un cajero automático y el saldo de la cuenta no se actualizó.", Impact.Medium, Urgency.Medium),
        ("transfers", "Transferencia no llega al destinatario", "Hice una transferencia interbancaria hace dos días y la otra persona todavía no la recibe.", Impact.Medium, Urgency.Medium),
        ("transfers", "No puedo agregar un beneficiario", "Al registrar la cuenta de un proveedor la app indica que los datos no coinciden.", Impact.High, Urgency.Medium),
        ("top_ups", "Recarga de saldo no aplicada", "Hice una recarga a mi celular desde la app y no se acreditó.", Impact.Low, Urgency.Low),
        ("fees_fx", "Cobro de comisión que no entiendo", "Me debitaron una comisión por mantenimiento que antes no me cobraban.", Impact.Low, Urgency.Low),
        ("fees_fx", "Tipo de cambio en compra al exterior", "Compré en una tienda del exterior y el valor cobrado es mayor al que esperaba.", Impact.Low, Urgency.Low),
        ("security_fraud", "Consumos que no reconozco", "Aparecen tres compras en línea que no hice. Necesito bloquear la tarjeta de inmediato.", Impact.High, Urgency.High),
        ("security_fraud", "Perdí mi celular con la app instalada", "Me robaron el teléfono y tengo la app con el acceso guardado.", Impact.High, Urgency.High),
        ("security_fraud", "Correo sospechoso a nombre de la entidad", "Recibí un correo que pide actualizar mis datos en un enlace extraño.", Impact.Medium, Urgency.Medium),
        ("account_kyc", "Actualizar datos de contacto", "Cambié de domicilio y necesito actualizar mi dirección y mi número de contacto.", Impact.Low, Urgency.Low),
        ("account_kyc", "Verificación de identidad rechazada", "Subí mis documentos para la verificación y fue rechazada sin explicación.", Impact.Medium, Urgency.Medium),
        ("account_kyc", "Cerrar mi cuenta de ahorros", "Quiero cerrar una cuenta que ya no uso. ¿Qué pasos debo seguir?", Impact.Low, Urgency.Low),
        (ReferenceData.GeneralCategoryCode, "Horario de atención en agencias", "¿Cuál es el horario de atención de las agencias durante el feriado?", Impact.Low, Urgency.Low),
        (ReferenceData.GeneralCategoryCode, "Solicitud de certificado bancario", "Necesito un certificado bancario para un trámite. ¿Cómo lo solicito?", Impact.Low, Urgency.Medium),
        ("transfers", "Transferencias masivas de nómina con error", "El archivo de pagos de nómina de la empresa fue rechazado y 40 empleados no recibieron su sueldo.", Impact.High, Urgency.High),
    ];

    private static readonly string[] StaffReplies =
    [
        "Gracias por escribirnos. Ya revisamos su caso y le mantendremos informado por este medio.",
        "Estamos validando la información con el área responsable. Le escribimos apenas tengamos novedades.",
        "Recibimos su solicitud y ya está en análisis.",
    ];

    private static readonly string[] HoldReasons =
    [
        "Se pidió al cliente el número de comprobante de la operación.",
        "Se espera la captura de pantalla del error.",
        "Se espera la respuesta del proveedor de la red de cajeros.",
    ];

    private static readonly string[] CustomerReplies =
    [
        "Les envío el número de comprobante que me pidieron.",
        "Volví a intentar y sigue igual.",
        "Gracias, quedo atento.",
    ];

    private static readonly Dictionary<string, string> Resolutions = new()
    {
        ["cards"] = "Se gestionó la reposición y se confirmó la entrega de la tarjeta.",
        ["digital_channels"] = "Se restableció el acceso y se validó el ingreso con el cliente.",
        ["card_payments"] = "Se verificó la operación con el comercio y se aplicó el ajuste correspondiente.",
        ["cash_atm"] = "Se conciliaron los registros del cajero y se acreditó el valor al cliente.",
        ["transfers"] = "Se rastreó la transferencia y se confirmó la acreditación en la cuenta destino.",
        ["top_ups"] = "Se reprocesó la recarga y se confirmó la acreditación.",
        ["fees_fx"] = "Se explicó el detalle del cargo y, al no corresponder, se devolvió la comisión.",
        ["security_fraud"] = "Se bloqueó la tarjeta, se emitió una nueva y se abrió el reclamo por consumos no reconocidos.",
        ["account_kyc"] = "Se actualizaron los datos tras validar la identidad del cliente.",
        [ReferenceData.GeneralCategoryCode] = "Se respondió la consulta con la información solicitada.",
    };

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Customers.AnyAsync(cancellationToken))
        {
            LogSkipped(logger);
            return;
        }

        var password = configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Define Seed__DemoPassword para crear los usuarios demo (o desactiva Database__SeedDemoData).");
        }

        var now = clock.GetUtcNow();
        var random = new Random(34);
        var teams = await db.Teams.Include(t => t.Members).ToListAsync(cancellationToken);
        var categories = await db.Categories.ToListAsync(cancellationToken);

        var staffIds = new Dictionary<string, Guid>();
        foreach (var person in Staff)
        {
            var user = await CreateUserAsync(person.Name, person.Email, person.Role, null, password, now.AddDays(-HistoryDays - 5), privacyAccepted: false);
            staffIds[person.Email] = user.Id;
            foreach (var code in person.Teams)
            {
                teams.First(t => t.Code == code).AddMember(user.Id, person.LeadOf.Contains(code), now.AddDays(-HistoryDays - 5));
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var customers = new List<(Customer Customer, Guid UserId)>();
        for (var i = 0; i < DemoCustomers.Length; i++)
        {
            var c = DemoCustomers[i];
            var customer = new Customer($"CLI-{i + 1:D5}", c.Name, c.Kind, c.Tier, c.UserEmail, null, now.AddDays(-HistoryDays - 5));
            db.Customers.Add(customer);
            await db.SaveChangesAsync(cancellationToken);
            var portalUser = await CreateUserAsync(c.UserName, c.UserEmail, RoleNames.Customer, customer.Id, password, now.AddDays(-HistoryDays - 5), privacyAccepted: i != 0);
            customers.Add((customer, portalUser.Id));
        }

        var supervisor = new Actor(staffIds[$"supervisor@{DemoDomain}"], ActorRole.Supervisor);
        var created = Enumerable.Range(0, TicketCount)
            .Select(_ => RandomCreation(random, now))
            .OrderBy(t => t)
            .ToList();

        foreach (var createdAt in created)
        {
            var template = Templates[random.Next(Templates.Length)];
            var (customer, customerUserId) = customers[random.Next(customers.Count)];
            var category = categories.First(c => c.Code == template.Category);
            var classified = random.NextDouble() < 0.8;
            var serviceDesk = teams.First(t => t.Code == ReferenceData.ServiceDeskTeamCode);
            var team = classified ? teams.First(t => t.Id == category.DefaultTeamId) : serviceDesk;
            var channel = random.NextDouble() < 0.7 ? TicketChannel.Portal : (TicketChannel)random.Next(1, 5);
            var createdBy = channel == TicketChannel.Portal ? customerUserId : PickAgent(team, random) ?? staffIds[$"agente@{DemoDomain}"];

            var priority = PriorityMatrix.Calculate(template.Impact, template.Urgency);
            var terms = await sla.SelectAsync(priority, customer.ServiceTier, cancellationToken);
            var ticket = Ticket.Open(
                new NewTicket(template.Title, template.Description, channel, template.Impact, template.Urgency, classified ? category.Id : null, team.Id, customer.Id, createdBy),
                terms,
                createdAt);

            await SimulateAsync(ticket, terms, template.Category, team, customerUserId, supervisor, random, now, cancellationToken);
            ticket.ClearDomainEvents();
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(cancellationToken);
        }

        LogSeeded(logger, TicketCount, customers.Count);
    }

    private async Task SimulateAsync(
        Ticket ticket,
        Domain.Sla.SlaTerms terms,
        string categoryCode,
        Domain.Catalog.Team team,
        Guid customerUserId,
        Actor supervisor,
        Random random,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fast = ticket.Priority <= Priority.High;
        var t = ticket.CreatedAt;

        if (random.NextDouble() < 0.04)
        {
            Cancel(ticket, new Actor(customerUserId, ActorRole.Customer), t.AddMinutes(random.Next(5, 60)), now);
            return;
        }

        var agentId = PickAgent(team, random);
        t = t.AddMinutes(fast ? random.Next(3, 40) : random.Next(10, 240));
        if (agentId is null || t > now)
        {
            return;
        }

        ticket.Assign(agentId.Value, supervisor, t);
        var agent = new Actor(agentId, ActorRole.Agent);

        t = t.AddMinutes(fast ? random.Next(2, 50) : random.Next(15, 420));
        if (t > now)
        {
            return;
        }

        ticket.Start(agent, terms, t);
        t = t.AddMinutes(random.Next(2, 30));
        if (t > now)
        {
            return;
        }

        ticket.AddComment(agentId.Value, ActorRole.Agent, StaffReplies[random.Next(StaffReplies.Length)], isInternal: false, terms, t);

        if (random.NextDouble() < 0.25)
        {
            t = t.AddMinutes(random.Next(20, 240));
            if (t > now)
            {
                return;
            }

            ticket.Hold(HoldReasons[random.Next(HoldReasons.Length)], agent, t);
            t = t.AddMinutes(random.Next(60, 2_000));
            if (t > now)
            {
                return;
            }

            ticket.AddComment(customerUserId, ActorRole.Customer, CustomerReplies[random.Next(CustomerReplies.Length)], isInternal: false, terms, t);
        }

        if (random.NextDouble() < 0.3)
        {
            ticket.AddComment(agentId.Value, ActorRole.Agent, "Se escaló la validación al proveedor; seguimiento interno.", isInternal: true, terms, t.AddMinutes(5));
        }

        t = t.AddMinutes(fast ? random.Next(30, 600) : random.Next(120, 4_000));
        if (t > now)
        {
            return;
        }

        var autoClose = await sla.AutoCloseAtAsync(t, cancellationToken);
        ticket.Resolve(Resolutions[categoryCode], agent, terms, autoClose, t);

        if (random.NextDouble() < 0.08 && t.AddHours(6) < now)
        {
            ticket.Reopen("El cliente indica que el problema continúa.", new Actor(customerUserId, ActorRole.Customer), terms, t.AddHours(6));
            t = t.AddHours(6).AddMinutes(random.Next(60, 900));
            if (t > now)
            {
                return;
            }

            autoClose = await sla.AutoCloseAtAsync(t, cancellationToken);
            ticket.Resolve(Resolutions[categoryCode] + " Se verificó de nuevo con el cliente.", agent, terms, autoClose, t);
        }

        var surveyAt = t.AddMinutes(random.Next(30, 2_500));
        if (random.NextDouble() < 0.55 && surveyAt <= now)
        {
            ticket.SubmitSurvey(customerUserId, Rating(random), random.NextDouble() < 0.4 ? "Atención clara y rápida." : null, surveyAt);
        }
        else if (autoClose.AddMinutes(1) <= now)
        {
            ticket.TryAutoClose(autoClose.AddMinutes(1));
        }
    }

    private static void Cancel(Ticket ticket, Actor customer, DateTimeOffset at, DateTimeOffset now)
    {
        if (at <= now)
        {
            ticket.Cancel("El cliente resolvió el problema por su cuenta.", customer, at);
        }
    }

    private static Guid? PickAgent(Domain.Catalog.Team team, Random random)
    {
        var members = team.Members.ToList();
        return members.Count == 0 ? null : members[random.Next(members.Count)].UserId;
    }

    /// <summary>Distribución de calificaciones: mayoría satisfechos, algunos neutros y pocos insatisfechos.</summary>
    private static int Rating(Random random) => random.NextDouble() switch
    {
        < 0.45 => 5,
        < 0.75 => 4,
        < 0.90 => 3,
        < 0.96 => 2,
        _ => 1,
    };

    /// <summary>Fecha de creación en los últimos 60 días, casi siempre en horario de oficina.</summary>
    private static DateTimeOffset RandomCreation(Random random, DateTimeOffset now)
    {
        var day = now.AddDays(-random.Next(0, HistoryDays)).ToOffset(TimeSpan.FromHours(-5)).Date;
        var hour = random.NextDouble() < 0.85 ? random.Next(8, 17) : random.Next(0, 24);
        var local = new DateTimeOffset(day.AddHours(hour).AddMinutes(random.Next(0, 60)), TimeSpan.FromHours(-5));
        return (local > now.AddMinutes(-20) ? now.AddMinutes(-random.Next(20, 600)) : local).ToUniversalTime();
    }

    private async Task<AppUser> CreateUserAsync(string name, string email, string role, Guid? customerId, string password, DateTimeOffset createdAt, bool privacyAccepted)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = name,
            CustomerId = customerId,
            CreatedAt = createdAt,
            PrivacyNoticeVersion = privacyAccepted ? Contracts.Common.PrivacyNotice.CurrentVersion : null,
            PrivacyNoticeAcceptedAt = privacyAccepted ? createdAt : null,
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("No se pudo crear un usuario demo: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Datos demo ya existentes: no se vuelven a crear")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Datos demo creados: {Tickets} tickets y {Customers} clientes ficticios")]
    private static partial void LogSeeded(ILogger logger, int tickets, int customers);
}
