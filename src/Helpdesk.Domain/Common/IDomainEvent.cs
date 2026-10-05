namespace Helpdesk.Domain.Common;

/// <summary>Hecho del dominio que otras partes del sistema (notificaciones, tiempo real) consumen después de guardar.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
