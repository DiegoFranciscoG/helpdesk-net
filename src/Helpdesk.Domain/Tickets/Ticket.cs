using Helpdesk.Domain.Attachments;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Sla;

namespace Helpdesk.Domain.Tickets;

/// <summary>Datos de apertura de un ticket.</summary>
public sealed record NewTicket(
    string Title,
    string Description,
    TicketChannel Channel,
    Impact Impact,
    Urgency Urgency,
    Guid? CategoryId,
    Guid TeamId,
    Guid CustomerId,
    Guid CreatedById);

/// <summary>
/// Agregado raíz del incidente. Concentra la máquina de estados (R5), la prioridad (R1) y el reloj de SLA
/// con pausas (R2, R6, R7, R8). Toda modificación pasa por sus métodos y deja historial y eventos.
/// </summary>
public sealed class Ticket : Entity
{
    public const int TitleMaxLength = 150;
    public const int DescriptionMaxLength = 8000;
    public const int ReasonMaxLength = 500;
    public const int SummaryMaxLength = 2000;

    private readonly List<TicketStatusChange> _history = [];
    private readonly List<TicketComment> _comments = [];
    private readonly List<TicketAttachment> _attachments = [];

    private Ticket()
    {
        Title = string.Empty;
        Description = string.Empty;
    }

    public long Number { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public TicketStatus Status { get; private set; }

    public TicketChannel Channel { get; private set; }

    public Impact Impact { get; private set; }

    public Urgency Urgency { get; private set; }

    public Priority Priority { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid TeamId { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid CreatedById { get; private set; }

    public Guid SlaPolicyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset FirstResponseDueAt { get; private set; }

    public DateTimeOffset FirstResponseWarningAt { get; private set; }

    public DateTimeOffset? FirstRespondedAt { get; private set; }

    public bool FirstResponseBreached { get; private set; }

    public bool ResponseWarningSent { get; private set; }

    public int? FirstResponseBusinessMinutes { get; private set; }

    public DateTimeOffset ResolutionDueAt { get; private set; }

    public DateTimeOffset ResolutionWarningAt { get; private set; }

    public DateTimeOffset? SlaPausedAt { get; private set; }

    public int SlaPausedBusinessMinutes { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool ResolutionBreached { get; private set; }

    public bool ResolutionWarningSent { get; private set; }

    public int? ResolutionBusinessMinutes { get; private set; }

    public string? ResolutionSummary { get; private set; }

    public int EscalationLevel { get; private set; }

    public int ReopenCount { get; private set; }

    public DateTimeOffset? AutoCloseAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Token de concurrencia optimista (columna de sistema <c>xmin</c> de PostgreSQL).</summary>
    public uint Version { get; private set; }

    public SatisfactionSurvey? Survey { get; private set; }

    public IReadOnlyCollection<TicketStatusChange> History => _history.AsReadOnly();

    public IReadOnlyCollection<TicketComment> Comments => _comments.AsReadOnly();

    public IReadOnlyCollection<TicketAttachment> Attachments => _attachments.AsReadOnly();

    public string DisplayNumber => FormatNumber(Number);

    public static string FormatNumber(long number) => $"HD-{number:D6}";

    public static Ticket Open(NewTicket data, SlaTerms terms, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(terms);

        var ticket = new Ticket
        {
            Title = Required(data.Title, TitleMaxLength, "title"),
            Description = Required(data.Description, DescriptionMaxLength, "description"),
            Channel = data.Channel,
            Impact = data.Impact,
            Urgency = data.Urgency,
            Priority = PriorityMatrix.Calculate(data.Impact, data.Urgency),
            CategoryId = data.CategoryId,
            TeamId = data.TeamId,
            CustomerId = data.CustomerId,
            CreatedById = data.CreatedById,
            Status = TicketStatus.New,
            CreatedAt = now,
            UpdatedAt = now,
        };

        ticket.ApplySlaTerms(terms, now);
        ticket._history.Add(new TicketStatusChange(ticket.Id, null, TicketStatus.New, data.CreatedById, null, now));
        ticket.Raise(new TicketCreated(ticket.Id, now));
        return ticket;
    }

    public IReadOnlyList<TicketTrigger> AvailableTriggers(ActorRole role) => TicketStateMachine.AvailableTriggers(Status, role);

    /// <summary>Asigna o reasigna. Desde <see cref="TicketStatus.New"/> pasa a <see cref="TicketStatus.Assigned"/>.</summary>
    public void Assign(Guid assigneeId, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Assign, actor.Role);
        if (AssigneeId == assigneeId)
        {
            throw new DomainException("already_assigned", "El ticket ya está asignado a esa persona.");
        }

        var previous = AssigneeId;
        AssigneeId = assigneeId;
        ChangeStatus(target, actor, null, now);
        Raise(new TicketAssigned(Id, assigneeId, previous, now));
    }

    /// <summary>Escalamiento funcional: cambia de cola, libera al asignado y vuelve a Nuevo (R8).</summary>
    public void Transfer(Guid teamId, string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Transfer, actor.Role);
        if (teamId == TeamId)
        {
            throw new DomainException("same_team", "El ticket ya está en esa cola.");
        }

        var fromTeam = TeamId;
        TeamId = teamId;
        AssigneeId = null;
        ChangeStatus(target, actor, Required(reason, ReasonMaxLength, "reason"), now, recordSameStatus: true);
        Raise(new TicketTransferred(Id, fromTeam, teamId, now));
    }

    public void Start(Actor actor, SlaTerms terms, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(terms);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Start, actor.Role);
        RegisterFirstResponse(terms, now);
        ChangeStatus(target, actor, null, now);
    }

    /// <summary>En espera del cliente o de un tercero: pausa el reloj de resolución (R6).</summary>
    public void Hold(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Hold, actor.Role);
        var why = Required(reason, ReasonMaxLength, "reason");
        SlaPausedAt = now;
        ChangeStatus(target, actor, why, now);
    }

    public void Resume(Actor actor, SlaTerms terms, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(terms);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Resume, actor.Role);
        EndPause(terms, now);
        ChangeStatus(target, actor, null, now);
    }

    /// <summary>Resuelve: detiene el reloj, evalúa el cumplimiento y programa el cierre automático (R9).</summary>
    public void Resolve(string summary, Actor actor, SlaTerms terms, DateTimeOffset autoCloseAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(terms);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Resolve, actor.Role);
        var text = Required(summary, SummaryMaxLength, "summary");

        RegisterFirstResponse(terms, now);
        ResolvedAt = now;
        ResolutionSummary = text;
        ResolutionBreached |= now > ResolutionDueAt;
        ResolutionBusinessMinutes = Math.Max(0, terms.Calendar.BusinessMinutesBetween(CreatedAt, now) - SlaPausedBusinessMinutes);
        SlaPausedAt = now;
        AutoCloseAt = autoCloseAt;
        ChangeStatus(target, actor, Truncate(text, ReasonMaxLength), now);
    }

    /// <summary>Reabre un ticket resuelto: el tiempo que estuvo resuelto cuenta como pausa (R6).</summary>
    public void Reopen(string reason, Actor actor, SlaTerms terms, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(terms);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Reopen, actor.Role);
        var why = Required(reason, ReasonMaxLength, "reason");

        EndPause(terms, now);
        ResolvedAt = null;
        ResolutionBusinessMinutes = null;
        ResolutionSummary = null;
        AutoCloseAt = null;
        ReopenCount++;
        ChangeStatus(target, actor, why, now);
    }

    public void Close(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Close, actor.Role);
        ClosedAt = now;
        AutoCloseAt = null;
        ChangeStatus(target, actor, actor.Role == ActorRole.System ? "Cierre automático: el cliente no reabrió el ticket." : null, now);
    }

    public void Cancel(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var target = TicketStateMachine.Fire(Status, TicketTrigger.Cancel, actor.Role);
        var why = Required(reason, ReasonMaxLength, "reason");
        ClosedAt = now;
        SlaPausedAt = null;
        AutoCloseAt = null;
        ChangeStatus(target, actor, why, now);
    }

    /// <summary>
    /// Cambia categoría, impacto y urgencia. Si cambia la prioridad se aplica la nueva regla de SLA y los
    /// vencimientos se recalculan desde la creación (los ya cumplidos no se tocan).
    /// </summary>
    public void Reclassify(Guid? categoryId, Impact impact, Urgency urgency, SlaTerms terms, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(terms);
        if (!actor.IsStaff)
        {
            throw new ActionNotPermittedException("Reclassify", actor.Role.ToString());
        }

        if (Status.IsTerminal() || Status == TicketStatus.Resolved)
        {
            throw new DomainException("ticket_not_editable", "Solo se reclasifican tickets abiertos.");
        }

        CategoryId = categoryId;
        Impact = impact;
        Urgency = urgency;
        var newPriority = PriorityMatrix.Calculate(impact, urgency);
        if (newPriority != Priority || terms.PolicyId != SlaPolicyId)
        {
            Priority = newPriority;
            ApplySlaTerms(terms, now);
        }

        Touch(now);
        Raise(new TicketReclassified(Id, Priority, now));
    }

    /// <summary>
    /// Agrega un comentario. El primer comentario público del personal cuenta como primera respuesta (R7);
    /// la respuesta del cliente a un ticket en espera lo reanuda.
    /// </summary>
    public TicketComment AddComment(Guid authorId, ActorRole authorRole, string body, bool isInternal, SlaTerms terms, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (Status.IsTerminal())
        {
            throw new DomainException("ticket_closed", "No se puede comentar un ticket cerrado o cancelado.");
        }

        var isStaff = authorRole is ActorRole.Admin or ActorRole.Supervisor or ActorRole.Agent;
        if (isInternal && !isStaff)
        {
            throw new ActionNotPermittedException("InternalComment", authorRole.ToString());
        }

        var comment = new TicketComment(Id, authorId, Required(body, TicketComment.BodyMaxLength, "body"), isInternal, now);
        _comments.Add(comment);

        if (isStaff && !isInternal)
        {
            RegisterFirstResponse(terms, now);
        }

        Touch(now);
        Raise(new TicketCommentAdded(Id, comment.Id, isInternal, authorRole, now));

        if (authorRole == ActorRole.Customer && Status == TicketStatus.OnHold)
        {
            Resume(Actor.SystemActor, terms, now);
        }

        return comment;
    }

    public void AddAttachment(TicketAttachment attachment, ActorRole uploaderRole)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (Status.IsTerminal())
        {
            throw new DomainException("ticket_closed", "No se pueden adjuntar archivos a un ticket cerrado o cancelado.");
        }

        if (attachment.IsInternal && uploaderRole is not (ActorRole.Admin or ActorRole.Supervisor or ActorRole.Agent))
        {
            throw new ActionNotPermittedException("InternalAttachment", uploaderRole.ToString());
        }

        if (_attachments.Count >= AttachmentRules.MaxPerTicket)
        {
            throw new DomainException("attachment_limit", $"El ticket ya tiene el máximo de {AttachmentRules.MaxPerTicket} adjuntos.");
        }

        _attachments.Add(attachment);
        Touch(attachment.CreatedAt);
    }

    /// <summary>Encuesta del cliente. Si el ticket estaba resuelto, calificarlo confirma la solución y lo cierra.</summary>
    public SatisfactionSurvey SubmitSurvey(Guid customerUserId, int rating, string? comment, DateTimeOffset now)
    {
        if (Survey is not null)
        {
            throw new DomainException("survey_exists", "Este ticket ya fue calificado.");
        }

        if (Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            throw new DomainException("survey_not_allowed", "Solo se califican tickets resueltos o cerrados.");
        }

        if (rating is < 1 or > 5)
        {
            throw new DomainException("invalid_rating", "La calificación va de 1 a 5.");
        }

        if (Status == TicketStatus.Resolved)
        {
            Close(new Actor(customerUserId, ActorRole.Customer), now);
        }

        Survey = new SatisfactionSurvey(Id, rating, string.IsNullOrWhiteSpace(comment) ? null : Truncate(comment.Trim(), SatisfactionSurvey.CommentMaxLength), customerUserId, now);
        return Survey;
    }

    /// <summary>
    /// Evalúa avisos e incumplimientos de SLA en el instante <paramref name="now"/> (R8). Es idempotente:
    /// cada aviso se emite una sola vez. Devuelve true si algo cambió.
    /// </summary>
    public bool EvaluateSla(DateTimeOffset now)
    {
        if (Status.IsTerminal())
        {
            return false;
        }

        var changed = false;
        if (FirstRespondedAt is null)
        {
            if (!ResponseWarningSent && now >= FirstResponseWarningAt)
            {
                ResponseWarningSent = true;
                Escalate(1);
                Raise(new SlaWarningRaised(Id, SlaMetric.FirstResponse, FirstResponseDueAt, now));
                changed = true;
            }

            if (!FirstResponseBreached && now > FirstResponseDueAt)
            {
                FirstResponseBreached = true;
                Escalate(2);
                Raise(new SlaBreached(Id, SlaMetric.FirstResponse, FirstResponseDueAt, now));
                changed = true;
            }
        }

        if (Status.IsResolutionClockRunning())
        {
            if (!ResolutionWarningSent && now >= ResolutionWarningAt)
            {
                ResolutionWarningSent = true;
                Escalate(1);
                Raise(new SlaWarningRaised(Id, SlaMetric.Resolution, ResolutionDueAt, now));
                changed = true;
            }

            if (!ResolutionBreached && now > ResolutionDueAt)
            {
                ResolutionBreached = true;
                Escalate(2);
                Raise(new SlaBreached(Id, SlaMetric.Resolution, ResolutionDueAt, now));
                changed = true;
            }
        }

        if (changed)
        {
            Touch(now);
        }

        return changed;
    }

    /// <summary>Cierre automático de tickets resueltos cuyo plazo venció (R9).</summary>
    public bool TryAutoClose(DateTimeOffset now)
    {
        if (Status != TicketStatus.Resolved || AutoCloseAt is null || AutoCloseAt > now)
        {
            return false;
        }

        Close(Actor.SystemActor, now);
        return true;
    }

    private void ApplySlaTerms(SlaTerms terms, DateTimeOffset now)
    {
        SlaPolicyId = terms.PolicyId;
        if (FirstRespondedAt is null)
        {
            FirstResponseDueAt = terms.Calendar.AddBusinessMinutes(CreatedAt, terms.FirstResponseMinutes);
            FirstResponseWarningAt = terms.Calendar.AddBusinessMinutes(CreatedAt, terms.WarningMinutes(terms.FirstResponseMinutes));
            ResponseWarningSent = FirstResponseWarningAt <= now && ResponseWarningSent;
            FirstResponseBreached = now > FirstResponseDueAt;
        }

        RecalculateResolutionTargets(terms, now);
    }

    private void RecalculateResolutionTargets(SlaTerms terms, DateTimeOffset now)
    {
        ResolutionDueAt = terms.Calendar.AddBusinessMinutes(CreatedAt, terms.ResolutionMinutes + SlaPausedBusinessMinutes);
        ResolutionWarningAt = terms.Calendar.AddBusinessMinutes(CreatedAt, terms.WarningMinutes(terms.ResolutionMinutes) + SlaPausedBusinessMinutes);
        if (ResolutionWarningAt > now)
        {
            ResolutionWarningSent = false;
        }

        if (ResolvedAt is null && Status.IsResolutionClockRunning())
        {
            ResolutionBreached = now > ResolutionDueAt;
        }
    }

    private void EndPause(SlaTerms terms, DateTimeOffset now)
    {
        if (SlaPausedAt is { } pausedAt)
        {
            SlaPausedBusinessMinutes += terms.Calendar.BusinessMinutesBetween(pausedAt, now);
            SlaPausedAt = null;
        }

        RecalculateResolutionTargets(terms, now);
    }

    private void RegisterFirstResponse(SlaTerms terms, DateTimeOffset now)
    {
        if (FirstRespondedAt is not null)
        {
            return;
        }

        FirstRespondedAt = now;
        FirstResponseBusinessMinutes = terms.Calendar.BusinessMinutesBetween(CreatedAt, now);
        FirstResponseBreached |= now > FirstResponseDueAt;
    }

    private void ChangeStatus(TicketStatus target, Actor actor, string? reason, DateTimeOffset now, bool recordSameStatus = false)
    {
        var from = Status;
        Status = target;
        Touch(now);
        if (from == target && !recordSameStatus)
        {
            return;
        }

        _history.Add(new TicketStatusChange(Id, from, target, actor.UserId, reason, now));
        if (from != target)
        {
            Raise(new TicketStatusChanged(Id, from, target, actor.Role, now));
        }
    }

    private void Escalate(int level) => EscalationLevel = Math.Max(EscalationLevel, level);

    private void Touch(DateTimeOffset now) => UpdatedAt = now;

    private static string Required(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainException($"{field}_required", $"El campo '{field}' es obligatorio.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{field}_too_long", $"El campo '{field}' admite máximo {maxLength} caracteres.");
        }

        return trimmed;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
