using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Catalog;

/// <summary>Categoría del ticket con su cola por defecto. Los códigos coinciden con el catálogo de P27.</summary>
public sealed class Category : Entity
{
    private Category()
    {
        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
    }

    public Category(Guid id, string code, string name, string description, Guid defaultTeamId)
    {
        Id = id;
        Code = code;
        Name = name;
        Description = description;
        DefaultTeamId = defaultTeamId;
        IsActive = true;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public Guid DefaultTeamId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string description, Guid defaultTeamId, bool isActive)
    {
        Name = name;
        Description = description;
        DefaultTeamId = defaultTeamId;
        IsActive = isActive;
    }
}
