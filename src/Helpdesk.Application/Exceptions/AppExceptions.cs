namespace Helpdesk.Application.Exceptions;

/// <summary>El recurso no existe o el usuario no puede verlo (se responde igual para no revelar su existencia, OWASP API1).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource)
        : base($"{resource} no encontrado.")
    {
    }

    public NotFoundException()
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>El usuario ve el recurso pero no puede ejecutar la acción (HTTP 403).</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message)
        : base(message)
    {
    }

    public ForbiddenException()
    {
    }

    public ForbiddenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>El recurso cambió mientras se editaba (concurrencia optimista, HTTP 409).</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("El ticket cambió mientras lo editabas. Recarga y vuelve a intentar.")
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>La versión enviada en If-Match no coincide (HTTP 412).</summary>
public sealed class PreconditionFailedException : Exception
{
    public PreconditionFailedException()
        : base("La versión del ticket no coincide con la enviada en If-Match.")
    {
    }

    public PreconditionFailedException(string message)
        : base(message)
    {
    }

    public PreconditionFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Error de negocio de la capa de aplicación con código estable (HTTP 422).</summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message)
        : base(message) => Code = code;

    public BusinessRuleException()
    {
        Code = "business_rule";
    }

    public BusinessRuleException(string message)
        : base(message) => Code = "business_rule";

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException) => Code = "business_rule";

    public string Code { get; } = "business_rule";
}

/// <summary>Credenciales o token inválidos. El mensaje es siempre genérico para no revelar si el usuario existe (HTTP 401).</summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException()
        : base("Credenciales inválidas.")
    {
    }

    public AuthenticationFailedException(string message)
        : base(message)
    {
    }

    public AuthenticationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
