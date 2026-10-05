namespace Helpdesk.Domain.Common;

/// <summary>Rol con el que se ejecuta una acción. <see cref="System"/> son los procesos automáticos.</summary>
public enum ActorRole
{
    Admin,
    Supervisor,
    Agent,
    Customer,
    Integration,
    System,
}

/// <summary>Quién ejecuta una acción sobre el dominio.</summary>
public sealed record Actor(Guid? UserId, ActorRole Role)
{
    public static Actor SystemActor { get; } = new(null, ActorRole.System);

    public bool IsStaff => Role is ActorRole.Admin or ActorRole.Supervisor or ActorRole.Agent;
}
