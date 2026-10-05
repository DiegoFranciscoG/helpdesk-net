using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Customers;

public enum CustomerKind
{
    Person,
    Organization,
}

/// <summary>Nivel de servicio contratado; selecciona la regla de SLA (R2).</summary>
public enum ServiceTier
{
    Standard,
    Premium,
}

/// <summary>
/// Cliente que solicita soporte. Por minimización (LOPDP art. 10 e) no guarda cédula ni RUC:
/// el <see cref="Code"/> lo identifica en reportes sin exponer datos personales.
/// </summary>
public sealed class Customer : Entity
{
    private Customer()
    {
        Code = string.Empty;
        DisplayName = string.Empty;
    }

    public Customer(string code, string displayName, CustomerKind kind, ServiceTier serviceTier, string? email, string? phone, DateTimeOffset now)
    {
        Code = code;
        DisplayName = displayName;
        Kind = kind;
        ServiceTier = serviceTier;
        Email = email;
        Phone = phone;
        IsActive = true;
        CreatedAt = now;
    }

    public string Code { get; private set; }

    public string DisplayName { get; private set; }

    public CustomerKind Kind { get; private set; }

    public ServiceTier ServiceTier { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? AnonymizedAt { get; private set; }

    public void Update(string displayName, CustomerKind kind, ServiceTier serviceTier, string? email, string? phone)
    {
        EnsureNotAnonymized();
        DisplayName = displayName;
        Kind = kind;
        ServiceTier = serviceTier;
        Email = email;
        Phone = phone;
    }

    /// <summary>
    /// Derecho de eliminación (LOPDP art. 15): borra los datos de contacto de forma irreversible y conserva
    /// los tickets para estadística, identificados solo por el código.
    /// </summary>
    public void Anonymize(DateTimeOffset now)
    {
        EnsureNotAnonymized();
        DisplayName = $"Cliente anonimizado {Code}";
        Email = null;
        Phone = null;
        IsActive = false;
        AnonymizedAt = now;
    }

    private void EnsureNotAnonymized()
    {
        if (AnonymizedAt is not null)
        {
            throw new DomainException("customer_anonymized", "El cliente fue anonimizado y no admite cambios.");
        }
    }
}
