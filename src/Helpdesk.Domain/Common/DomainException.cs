namespace Helpdesk.Domain.Common;

/// <summary>Violación de una regla de negocio. <see cref="Code"/> es estable y se devuelve al cliente.</summary>
public class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message) => Code = code;

    public DomainException()
        : this("domain_error", "Regla de negocio incumplida.")
    {
    }

    public DomainException(string message)
        : this("domain_error", message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException) => Code = "domain_error";

    public string Code { get; }
}

/// <summary>La transición pedida no existe desde el estado actual (HTTP 409).</summary>
public sealed class InvalidTransitionException : DomainException
{
    public InvalidTransitionException(string from, string trigger)
        : base("invalid_transition", $"No se puede ejecutar '{trigger}' desde el estado '{from}'.")
    {
    }

    public InvalidTransitionException()
    {
    }

    public InvalidTransitionException(string message)
        : base(message)
    {
    }

    public InvalidTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>El rol del actor no puede ejecutar la acción (HTTP 403).</summary>
public sealed class ActionNotPermittedException : DomainException
{
    public ActionNotPermittedException(string action, string role)
        : base("action_not_permitted", $"El rol '{role}' no puede ejecutar '{action}'.")
    {
    }

    public ActionNotPermittedException()
    {
    }

    public ActionNotPermittedException(string message)
        : base(message)
    {
    }

    public ActionNotPermittedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
