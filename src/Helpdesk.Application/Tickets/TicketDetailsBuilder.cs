using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Security;
using Helpdesk.Application.Sla;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Application.Tickets;

/// <summary>Arma el detalle de un ticket resolviendo nombres de usuarios, cola, categoría, cliente y regla de SLA.</summary>
public sealed class TicketDetailsBuilder(
    IUserDirectory users,
    ICatalogRepository catalog,
    ICustomerRepository customers,
    ISlaTermsProvider sla,
    TimeProvider clock)
{
    public async Task<TicketDetailDto> BuildAsync(Ticket ticket, AccessContext access, CancellationToken cancellationToken)
    {
        var lookups = await LookupsAsync(ticket, cancellationToken);
        return TicketMapper.ToDetail(ticket, lookups, access, clock.GetUtcNow());
    }

    public async Task<TicketLookups> LookupsAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var userIds = new HashSet<Guid> { ticket.CreatedById };
        if (ticket.AssigneeId is { } assignee)
        {
            userIds.Add(assignee);
        }

        userIds.UnionWith(ticket.Comments.Select(c => c.AuthorId));
        userIds.UnionWith(ticket.Attachments.Select(a => a.UploadedById));
        userIds.UnionWith(ticket.History.Where(h => h.ChangedById is not null).Select(h => h.ChangedById!.Value));

        var names = await users.GetNamesAsync(userIds, cancellationToken);
        var customerUsers = (await users.GetCustomerUserIdsAsync(ticket.CustomerId, cancellationToken)).ToHashSet();
        var team = await catalog.GetTeamAsync(ticket.TeamId, cancellationToken);
        var category = ticket.CategoryId is { } categoryId ? await catalog.GetCategoryAsync(categoryId, cancellationToken) : null;
        var customer = await customers.GetAsync(ticket.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("El ticket referencia un cliente inexistente.");
        var policyName = await sla.PolicyNameAsync(ticket.SlaPolicyId, cancellationToken);

        return new TicketLookups(names, customerUsers, team?.Name ?? "—", category?.Name, customer, policyName);
    }
}
