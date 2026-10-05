using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Exceptions;
using Helpdesk.Application.Sla;
using Helpdesk.Contracts.Catalog;
using Helpdesk.Contracts.Common;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Application.Catalog;

/// <summary>Colas, categorías y personal asignable.</summary>
public sealed class CatalogService(ICatalogRepository catalog, IUserDirectory users, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<IReadOnlyList<TeamDto>> ListTeamsAsync(CancellationToken cancellationToken)
    {
        var teams = await catalog.ListTeamsAsync(includeMembers: true, cancellationToken);
        var names = await users.GetNamesAsync(teams.SelectMany(t => t.Members.Select(m => m.UserId)), cancellationToken);
        return [.. teams.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => new TeamDto(
            t.Id, t.Code, t.Name, t.Description, t.IsActive,
            [.. t.Members.Select(m => new TeamMemberDto(m.UserId, names.GetValueOrDefault(m.UserId, "Usuario"), m.IsLead)).OrderBy(m => m.FullName, StringComparer.Ordinal)]))];
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        [.. (await catalog.ListCategoriesAsync(cancellationToken))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Description, c.DefaultTeamId, c.IsActive))];

    public async Task<IReadOnlyList<AgentDto>> ListAgentsAsync(Guid? teamId, CancellationToken cancellationToken) =>
        [.. (await users.ListStaffAsync(teamId, cancellationToken))
            .OrderBy(s => s.FullName, StringComparer.Ordinal)
            .Select(s => new AgentDto(s.Id, s.FullName, s.Role.ToString(), [.. s.TeamIds]))];

    public async Task<TeamDto> SetMembersAsync(Guid teamId, SetTeamMembersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var team = await catalog.GetTeamAsync(teamId, cancellationToken) ?? throw new NotFoundException("Cola");
        // Solo se valida a quien se agrega: un miembro existente desactivado no bloquea la edición de la cola.
        foreach (var member in request.Members.Where(m => !team.HasMember(m.UserId)))
        {
            if (await users.GetActiveStaffAsync(member.UserId, cancellationToken) is null)
            {
                throw new BusinessRuleException("member_not_staff", "Solo personal interno activo puede pertenecer a una cola.");
            }
        }

        foreach (var removed in team.Members.Where(m => request.Members.All(r => r.UserId != m.UserId)).ToList())
        {
            team.RemoveMember(removed.UserId);
        }

        foreach (var member in request.Members)
        {
            team.AddMember(member.UserId, member.IsLead, clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await ListTeamsAsync(cancellationToken)).First(t => t.Id == teamId);
    }

    public static IReadOnlyList<PriorityMatrixCellDto> PriorityMatrixCells() =>
        [.. Enum.GetValues<Impact>().SelectMany(i => Enum.GetValues<Urgency>().Select(u => new PriorityMatrixCellDto(i, u, PriorityMatrix.Calculate(i, u))))];
}

/// <summary>Clientes: alta y edición por supervisión; anonimización (derecho de eliminación) por administración.</summary>
public sealed class CustomerService(
    ICustomerRepository customers,
    IUserAdminService userAdmin,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<PagedResult<CustomerDto>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var result = await customers.SearchAsync(search?.Trim(), Math.Max(1, page), Math.Clamp(pageSize, 1, 100), cancellationToken);
        return new PagedResult<CustomerDto>([.. result.Items.Select(ToDto)], result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToDto(await customers.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Cliente"));

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var customer = new Customer(await customers.NextCodeAsync(cancellationToken), request.DisplayName.Trim(), request.Kind, request.ServiceTier, Clean(request.Email), Clean(request.Phone), clock.GetUtcNow());
        customers.Add(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(customer);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var customer = await customers.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Cliente");
        customer.Update(request.DisplayName.Trim(), request.Kind, request.ServiceTier, Clean(request.Email), Clean(request.Phone));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(customer);
    }

    /// <summary>
    /// LOPDP art. 15: borra contacto del cliente y seudonimiza a sus usuarios del portal. Los tickets se conservan
    /// para estadística, identificados solo por el código del cliente.
    /// </summary>
    public async Task<CustomerDto> AnonymizeAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Cliente");
        customer.Anonymize(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await userAdmin.AnonymizeCustomerUsersAsync(customer.Id, cancellationToken);
        return ToDto(customer);
    }

    public static CustomerDto ToDto(Customer c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new CustomerDto(c.Id, c.Code, c.DisplayName, c.Kind, c.ServiceTier, c.Email, c.Phone, c.IsActive, c.AnonymizedAt);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Administración de reglas de SLA y feriados.</summary>
public sealed class SlaAdminService(ISlaRepository repository, ISlaTermsProvider terms, IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<SlaPolicyDto>> ListPoliciesAsync(CancellationToken cancellationToken)
    {
        var calendars = await repository.ListCalendarsAsync(cancellationToken);
        return [.. (await repository.ListPoliciesAsync(cancellationToken))
            .OrderBy(p => p.Priority).ThenBy(p => p.ServiceTier)
            .Select(p => ToDto(p, calendars))];
    }

    /// <summary>Cambia objetivos. Aplica a los tickets nuevos o reclasificados (los vencimientos existentes no se reescriben).</summary>
    public async Task<SlaPolicyDto> UpdatePolicyAsync(Guid id, UpdateSlaPolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var policy = await repository.GetPolicyAsync(id, cancellationToken) ?? throw new NotFoundException("Regla de SLA");
        policy.ChangeTargets(request.FirstResponseMinutes, request.ResolutionMinutes, request.WarningPercent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        terms.Invalidate();
        return ToDto(policy, await repository.ListCalendarsAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<CalendarDto>> ListCalendarsAsync(CancellationToken cancellationToken) =>
        [.. (await repository.ListCalendarsAsync(cancellationToken)).OrderBy(c => c.Is24x7).Select(ToDto)];

    public async Task<HolidayDto> AddHolidayAsync(Guid calendarId, CreateHolidayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var calendar = await repository.GetCalendarAsync(calendarId, cancellationToken) ?? throw new NotFoundException("Calendario");
        if (calendar.Is24x7)
        {
            throw new BusinessRuleException("calendar_24x7", "Un calendario 24×7 no tiene feriados.");
        }

        var holiday = calendar.AddHoliday(request.Date, request.Name.Trim(), request.LegalBasis.Trim());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        terms.Invalidate();
        return new HolidayDto(holiday.Id, holiday.Date, holiday.Name, holiday.LegalBasis);
    }

    public async Task RemoveHolidayAsync(Guid calendarId, Guid holidayId, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarAsync(calendarId, cancellationToken) ?? throw new NotFoundException("Calendario");
        calendar.RemoveHoliday(holidayId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        terms.Invalidate();
    }

    private static SlaPolicyDto ToDto(SlaPolicy p, IReadOnlyList<BusinessCalendar> calendars) =>
        new(p.Id, p.Name, p.Priority, p.ServiceTier, p.FirstResponseMinutes, p.ResolutionMinutes, p.WarningPercent, p.CalendarId, calendars.FirstOrDefault(c => c.Id == p.CalendarId)?.Name ?? string.Empty, p.IsActive);

    private static CalendarDto ToDto(BusinessCalendar c) =>
        new(c.Id, c.Code, c.Name, c.TimeZoneId, c.Is24x7,
            [.. c.Hours.OrderBy(h => h.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)h.DayOfWeek).ThenBy(h => h.StartTime).Select(h => new BusinessHoursDto(h.DayOfWeek, h.StartTime, h.EndTime))],
            [.. c.Holidays.OrderBy(h => h.Date).Select(h => new HolidayDto(h.Id, h.Date, h.Name, h.LegalBasis))]);
}

/// <summary>Evalúa avisos e incumplimientos de SLA y cierres automáticos (lo invoca un servicio en segundo plano).</summary>
public sealed class SlaMonitorService(ITicketRepository tickets, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public const int BatchSize = 200;

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await tickets.GetDueForSlaEvaluationAsync(now, BatchSize, cancellationToken);
        var changed = 0;
        foreach (var ticket in due)
        {
            var evaluated = ticket.EvaluateSla(now);
            var closed = ticket.TryAutoClose(now);
            if (evaluated || closed)
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }
}
