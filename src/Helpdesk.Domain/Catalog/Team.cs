using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Catalog;

/// <summary>Cola o equipo de atención. Los códigos coinciden con el catálogo de P27 para enrutar sugerencias.</summary>
public sealed class Team : Entity
{
    private readonly List<TeamMember> _members = [];

    private Team()
    {
        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
    }

    public Team(Guid id, string code, string name, string description, DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        Name = name;
        Description = description;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<TeamMember> Members => _members.AsReadOnly();

    public void Update(string name, string description, bool isActive)
    {
        Name = name;
        Description = description;
        IsActive = isActive;
    }

    public void AddMember(Guid userId, bool isLead, DateTimeOffset now)
    {
        var existing = _members.FirstOrDefault(m => m.UserId == userId);
        if (existing is not null)
        {
            existing.SetLead(isLead);
            return;
        }

        _members.Add(new TeamMember(Id, userId, isLead, now));
    }

    public void RemoveMember(Guid userId) => _members.RemoveAll(m => m.UserId == userId);

    public bool HasMember(Guid userId) => _members.Any(m => m.UserId == userId);
}

/// <summary>Pertenencia de un agente a un equipo; los líderes reciben el escalamiento jerárquico (R8).</summary>
public sealed class TeamMember
{
    private TeamMember()
    {
    }

    public TeamMember(Guid teamId, Guid userId, bool isLead, DateTimeOffset joinedAt)
    {
        TeamId = teamId;
        UserId = userId;
        IsLead = isLead;
        JoinedAt = joinedAt;
    }

    public Guid TeamId { get; private set; }

    public Guid UserId { get; private set; }

    public bool IsLead { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    internal void SetLead(bool isLead) => IsLead = isLead;
}
