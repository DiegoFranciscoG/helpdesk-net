using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Exceptions;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Persistence;

/// <summary>
/// Guarda los cambios y después despacha los eventos de dominio: primero se confirma el dato, luego se generan
/// las notificaciones (segundo guardado) y por último se envía el tiempo real.
/// </summary>
internal sealed class EfUnitOfWork(HelpdeskDbContext db, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var entities = db.ChangeTracker.Entries<Entity>().Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList();
        var envelopes = entities.SelectMany(e => e.DomainEvents.Select(ev => new DomainEventEnvelope(e, ev))).ToList();
        entities.ForEach(e => e.ClearDomainEvents());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            if (envelopes.Count > 0)
            {
                await dispatcher.DispatchAsync(envelopes, cancellationToken);
            }

            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("El ticket cambió mientras lo editabas. Recarga y vuelve a intentar.", ex);
        }

        await dispatcher.PublishPendingAsync(cancellationToken);
    }

    public void ExpectVersion(Ticket ticket, uint expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (ticket.Version != expectedVersion)
        {
            throw new PreconditionFailedException();
        }

        db.Entry(ticket).Property(t => t.Version).OriginalValue = expectedVersion;
    }
}
