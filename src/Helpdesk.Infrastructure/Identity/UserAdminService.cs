using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Exceptions;
using Helpdesk.Contracts.Catalog;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Identity;

internal sealed class UserAdminService(
    UserManager<AppUser> userManager,
    HelpdeskDbContext db,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider clock) : IUserAdminService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await (from u in db.Users.AsNoTracking()
                          join ur in db.UserRoles on u.Id equals ur.UserId
                          join r in db.Roles on ur.RoleId equals r.Id
                          orderby u.FullName
                          select new { u.Id, u.FullName, u.Email, Role = r.Name!, u.CustomerId, u.IsActive, u.LastLoginAt }).ToListAsync(cancellationToken);
        var memberships = await db.TeamMembers.AsNoTracking().ToListAsync(cancellationToken);
        return [.. rows.Select(r => new UserDto(r.Id, r.FullName, r.Email ?? string.Empty, r.Role, r.CustomerId, r.IsActive, [.. memberships.Where(m => m.UserId == r.Id).Select(m => m.TeamId)], r.LastLoginAt))];
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CustomerId is { } customerId && !await db.Customers.AnyAsync(c => c.Id == customerId && c.IsActive, cancellationToken))
        {
            throw new BusinessRuleException("customer_invalid", "El cliente no existe o está inactivo.");
        }

        var teams = await db.Teams.Include(t => t.Members).Where(t => request.TeamIds.Contains(t.Id)).ToListAsync(cancellationToken);
        if (teams.Count != request.TeamIds.Distinct().Count())
        {
            throw new BusinessRuleException("team_invalid", "Alguna cola no existe.");
        }

        var email = request.Email.Trim();
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            CustomerId = request.CustomerId,
            CreatedAt = clock.GetUtcNow(),
        };

        var created = await userManager.CreateAsync(user, request.Password);
        created.EnsureSucceeded();

        (await userManager.AddToRoleAsync(user, request.Role)).EnsureSucceeded();
        foreach (var team in teams)
        {
            team.AddMember(user.Id, isLead: false, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        return new UserDto(user.Id, user.FullName, email, request.Role, user.CustomerId, user.IsActive, [.. teams.Select(t => t.Id)], null);
    }

    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        if (userId == currentUser.UserId && !isActive)
        {
            throw new BusinessRuleException("self_deactivation", "No puedes desactivar tu propia cuenta.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Usuario");
        user.IsActive = isActive;
        (await userManager.UpdateAsync(user)).EnsureSucceeded();
        if (!isActive)
        {
            (await userManager.UpdateSecurityStampAsync(user)).EnsureSucceeded();
            await RevokeSessionsAsync([user.Id], cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Guid>> AnonymizeCustomerUsersAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var users = await db.Users.Where(u => u.CustomerId == customerId).ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            var alias = $"anon-{user.Id:N}@anon.invalid";
            user.FullName = "Usuario anonimizado";
            user.Email = alias;
            user.NormalizedEmail = alias.ToUpperInvariant();
            user.UserName = alias;
            user.NormalizedUserName = alias.ToUpperInvariant();
            user.PhoneNumber = null;
            user.PasswordHash = null;
            user.IsActive = false;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        var ids = users.Select(u => u.Id).ToArray();
        await RevokeSessionsAsync(ids, cancellationToken);

        // Los archivos que subió el cliente pueden contener datos personales: se eliminan del almacenamiento.
        var attachments = await db.TicketAttachments.Where(a => ids.Contains(a.UploadedById)).ToListAsync(cancellationToken);
        db.TicketAttachments.RemoveRange(attachments);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var attachment in attachments)
        {
            await storage.DeleteAsync(attachment.StorageKey, cancellationToken);
        }

        return ids;
    }

    private async Task RevokeSessionsAsync(Guid[] userIds, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = await db.RefreshTokens.Where(t => userIds.Contains(t.UserId) && t.RevokedAt == null).ToListAsync(cancellationToken);
        active.ForEach(t => t.Revoke("user_deactivated", now));
        await db.SaveChangesAsync(cancellationToken);
    }
}
