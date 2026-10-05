using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Contracts.Catalog;

public sealed record TeamMemberDto(Guid UserId, string FullName, bool IsLead);

public sealed record TeamDto(Guid Id, string Code, string Name, string Description, bool IsActive, IReadOnlyList<TeamMemberDto> Members);

public sealed record CategoryDto(Guid Id, string Code, string Name, string Description, Guid DefaultTeamId, bool IsActive);

public sealed record AgentDto(Guid Id, string FullName, string Role, IReadOnlyList<Guid> TeamIds);

public sealed record CustomerDto(
    Guid Id,
    string Code,
    string DisplayName,
    CustomerKind Kind,
    ServiceTier ServiceTier,
    string? Email,
    string? Phone,
    bool IsActive,
    DateTimeOffset? AnonymizedAt);

public sealed class SaveCustomerRequest
{
    public string DisplayName { get; set; } = string.Empty;

    public CustomerKind Kind { get; set; } = CustomerKind.Person;

    public ServiceTier ServiceTier { get; set; } = ServiceTier.Standard;

    public string? Email { get; set; }

    public string? Phone { get; set; }
}

public sealed record UserDto(Guid Id, string FullName, string Email, string Role, Guid? CustomerId, bool IsActive, IReadOnlyList<Guid> TeamIds, DateTimeOffset? LastLoginAt);

public sealed class CreateUserRequest
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = "Agent";

    public Guid? CustomerId { get; set; }

    public List<Guid> TeamIds { get; set; } = [];
}

public sealed class SetUserStatusRequest
{
    public bool IsActive { get; set; }
}

public sealed class SetTeamMembersRequest
{
    public List<TeamMemberRequest> Members { get; set; } = [];
}

public sealed class TeamMemberRequest
{
    public Guid UserId { get; set; }

    public bool IsLead { get; set; }
}

public sealed record SlaPolicyDto(
    Guid Id,
    string Name,
    Priority Priority,
    ServiceTier? ServiceTier,
    int FirstResponseMinutes,
    int ResolutionMinutes,
    int WarningPercent,
    Guid CalendarId,
    string CalendarName,
    bool IsActive);

public sealed class UpdateSlaPolicyRequest
{
    public int FirstResponseMinutes { get; set; }

    public int ResolutionMinutes { get; set; }

    public int WarningPercent { get; set; } = 75;
}

public sealed record BusinessHoursDto(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

public sealed record HolidayDto(Guid Id, DateOnly Date, string Name, string LegalBasis);

public sealed record CalendarDto(Guid Id, string Code, string Name, string TimeZone, bool Is24x7, IReadOnlyList<BusinessHoursDto> Hours, IReadOnlyList<HolidayDto> Holidays);

public sealed class CreateHolidayRequest
{
    public DateOnly Date { get; set; }

    public string Name { get; set; } = string.Empty;

    public string LegalBasis { get; set; } = string.Empty;
}

/// <summary>Una celda de la matriz ITIL para mostrarla en el front.</summary>
public sealed record PriorityMatrixCellDto(Impact Impact, Urgency Urgency, Priority Priority);
