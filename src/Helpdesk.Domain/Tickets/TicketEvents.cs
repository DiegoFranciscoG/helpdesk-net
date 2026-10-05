using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Tickets;

public enum SlaMetric
{
    FirstResponse,
    Resolution,
}

public sealed record TicketCreated(Guid TicketId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record TicketAssigned(Guid TicketId, Guid AssigneeId, Guid? PreviousAssigneeId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record TicketStatusChanged(Guid TicketId, TicketStatus From, TicketStatus To, ActorRole ChangedBy, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record TicketTransferred(Guid TicketId, Guid FromTeamId, Guid ToTeamId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record TicketReclassified(Guid TicketId, Priority Priority, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record TicketCommentAdded(Guid TicketId, Guid CommentId, bool IsInternal, ActorRole AuthorRole, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SlaWarningRaised(Guid TicketId, SlaMetric Metric, DateTimeOffset DueAt, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SlaBreached(Guid TicketId, SlaMetric Metric, DateTimeOffset DueAt, DateTimeOffset OccurredAt) : IDomainEvent;
