using Helpdesk.Application.Abstractions;
using Helpdesk.Domain.Common;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Identity;

/// <summary>Consultas sobre personas (Identity + colas) que necesita la capa de aplicación.</summary>
internal sealed class UserDirectory(HelpdeskDbContext db) : IUserDirectory
{
    private static readonly string[] StaffRoles = [RoleNames.Admin, RoleNames.Supervisor, RoleNames.Agent];

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }

    public Task<string?> GetPrivacyNoticeVersionAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PrivacyNoticeVersion).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetTeamIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.TeamMembers.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.TeamId).ToListAsync(cancellationToken)).ToHashSet();

    public async Task<StaffMember?> GetActiveStaffAsync(Guid userId, CancellationToken cancellationToken)
    {
        var staff = await QueryStaffAsync(userId, null, cancellationToken);
        return staff.Count > 0 ? staff[0] : null;
    }

    public async Task<IReadOnlyList<Guid>> GetTeamLeadIdsAsync(Guid teamId, CancellationToken cancellationToken) =>
        await (from m in db.TeamMembers
               join u in db.Users on m.UserId equals u.Id
               where m.TeamId == teamId && m.IsLead && u.IsActive
               select u.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetSupervisorIdsAsync(CancellationToken cancellationToken) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               join u in db.Users on ur.UserId equals u.Id
               where r.Name == RoleNames.Supervisor && u.IsActive
               select u.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetCustomerUserIdsAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(u => u.CustomerId == customerId).Select(u => u.Id).ToListAsync(cancellationToken);

    public Task<IReadOnlyList<StaffMember>> ListStaffAsync(Guid? teamId, CancellationToken cancellationToken) =>
        QueryStaffAsync(null, teamId, cancellationToken);

    private async Task<IReadOnlyList<StaffMember>> QueryStaffAsync(Guid? userId, Guid? teamId, CancellationToken cancellationToken)
    {
        var rows = await (from u in db.Users.AsNoTracking()
                          join ur in db.UserRoles on u.Id equals ur.UserId
                          join r in db.Roles on ur.RoleId equals r.Id
                          where u.IsActive && StaffRoles.Contains(r.Name!) && (userId == null || u.Id == userId)
                          select new { u.Id, u.FullName, Role = r.Name! }).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToArray();
        var memberships = await db.TeamMembers.AsNoTracking().Where(m => ids.Contains(m.UserId)).ToListAsync(cancellationToken);

        return [.. rows
            .Select(r => new StaffMember(r.Id, r.FullName, Enum.Parse<ActorRole>(r.Role), memberships.Where(m => m.UserId == r.Id).Select(m => m.TeamId).ToHashSet()))
            .Where(s => teamId is null || s.TeamIds.Contains(teamId.Value))];
    }
}
