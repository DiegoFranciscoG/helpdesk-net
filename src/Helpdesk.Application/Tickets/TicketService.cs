using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Exceptions;
using Helpdesk.Application.Security;
using Helpdesk.Application.Sla;
using Helpdesk.Contracts.Common;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Tickets;
using Microsoft.Extensions.Logging;

namespace Helpdesk.Application.Tickets;

/// <summary>Casos de uso de tickets: apertura, consulta, asignación, transiciones, clasificación, comentarios y encuesta.</summary>
public sealed partial class TicketService(
    ITicketRepository tickets,
    ITicketQueries queries,
    ICatalogRepository catalog,
    ICustomerRepository customers,
    IUserDirectory users,
    ISlaTermsProvider sla,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TicketDetailsBuilder details,
    ISuggestionInbox suggestionInbox,
    INlpClassifier classifier,
    TimeProvider clock,
    ILogger<TicketService> logger)
{
    public const string DefaultTeamCode = "service_desk";

    public async Task<PagedResult<TicketListItemDto>> ListAsync(TicketQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await currentUser.GetAccessAsync(cancellationToken);
        return await queries.ListAsync(query, access, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<TicketDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var (ticket, access) = await LoadVisibleAsync(id, cancellationToken);
        return await details.BuildAsync(ticket, access, cancellationToken);
    }

    public async Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var access = await currentUser.GetAccessAsync(cancellationToken);
        if (access.Role == ActorRole.Customer && await users.GetPrivacyNoticeVersionAsync(currentUser.UserId, cancellationToken) != PrivacyNotice.CurrentVersion)
        {
            // LOPDP arts. 10 c y 12: el titular conoce el tratamiento antes de entregar sus datos. Se consulta la base y no
            // el token: el access token emitido antes de aceptar sigue vigente hasta 15 minutos.
            throw new BusinessRuleException("privacy_notice_required", "Acepta el aviso de privacidad antes de abrir un ticket.");
        }

        var customerId = ResolveCustomer(request, access);
        var customer = await customers.GetAsync(customerId, cancellationToken);
        if (customer is null || !customer.IsActive)
        {
            throw new BusinessRuleException("customer_inactive", "El cliente no existe o está inactivo.");
        }

        Guid teamId;
        if (request.CategoryId is { } categoryId)
        {
            var category = await catalog.GetCategoryAsync(categoryId, cancellationToken);
            if (category is null || !category.IsActive)
            {
                throw new BusinessRuleException("category_invalid", "La categoría no existe o está inactiva.");
            }

            teamId = category.DefaultTeamId;
        }
        else
        {
            var teams = await catalog.ListTeamsAsync(includeMembers: false, cancellationToken);
            teamId = teams.FirstOrDefault(t => t.Code == DefaultTeamCode)?.Id
                ?? throw new InvalidOperationException($"No existe la cola por defecto '{DefaultTeamCode}'.");
        }

        var channel = access.Role == ActorRole.Customer ? TicketChannel.Portal : request.Channel;
        var priority = PriorityMatrix.Calculate(request.Impact, request.Urgency);
        var terms = await sla.SelectAsync(priority, customer.ServiceTier, cancellationToken);
        var data = new NewTicket(request.Title, request.Description, channel, request.Impact, request.Urgency, request.CategoryId, teamId, customer.Id, access.UserId);
        var ticket = Ticket.Open(data, terms, clock.GetUtcNow());

        tickets.Add(ticket);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogTicketCreated(logger, ticket.Id, ticket.Priority);

        if (classifier.IsEnabled)
        {
            suggestionInbox.Enqueue(ticket.Id);
        }

        return await details.BuildAsync(ticket, access, cancellationToken);
    }

    public async Task<TicketDetailDto> AssignAsync(Guid id, AssignRequest request, uint? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (ticket, access) = await LoadVisibleAsync(id, cancellationToken, expectedVersion);
        EnsureManage(ticket, access);

        var assignee = await users.GetActiveStaffAsync(request.AssigneeId, cancellationToken);
        if (assignee is null || !assignee.TeamIds.Contains(ticket.TeamId))
        {
            throw new BusinessRuleException("assignee_not_in_team", "Solo se puede asignar a personal activo de la cola del ticket.");
        }

        ticket.Assign(assignee.Id, access.Actor, clock.GetUtcNow());
        return await SaveAndBuildAsync(ticket, access, cancellationToken);
    }

    public async Task<TicketDetailDto> TransferAsync(Guid id, TransferRequest request, uint? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (ticket, access) = await LoadVisibleAsync(id, cancellationToken, expectedVersion);
        EnsureManage(ticket, access);

        var team = await catalog.GetTeamAsync(request.TeamId, cancellationToken);
        if (team is null || !team.IsActive)
        {
            throw new BusinessRuleException("team_invalid", "La cola de destino no existe o está inactiva.");
        }

        ticket.Transfer(team.Id, request.Reason, access.Actor, clock.GetUtcNow());
        return await SaveAndBuildAsync(ticket, access, cancellationToken);
    }

    public async Task<TicketDetailDto> TransitionAsync(Guid id, TransitionRequest request, uint? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (ticket, access) = await LoadVisibleAsync(id, cancellationToken, expectedVersion);
        EnsureCanTransition(ticket, access, request.Action);

        var now = clock.GetUtcNow();
        var actor = access.Actor;
        var terms = await sla.ForPolicyAsync(ticket.SlaPolicyId, cancellationToken);
        switch (request.Action)
        {
            case TransitionAction.Start:
                ticket.Start(actor, terms, now);
                break;
            case TransitionAction.Hold:
                ticket.Hold(request.Reason ?? string.Empty, actor, now);
                break;
            case TransitionAction.Resume:
                ticket.Resume(actor, terms, now);
                break;
            case TransitionAction.Resolve:
                ticket.Resolve(request.ResolutionSummary ?? string.Empty, actor, terms, await sla.AutoCloseAtAsync(now, cancellationToken), now);
                break;
            case TransitionAction.Reopen:
                ticket.Reopen(request.Reason ?? string.Empty, actor, terms, now);
                break;
            case TransitionAction.Close:
                ticket.Close(actor, now);
                break;
            case TransitionAction.Cancel:
                ticket.Cancel(request.Reason ?? string.Empty, actor, now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Action, "Acción no soportada.");
        }

        return await SaveAndBuildAsync(ticket, access, cancellationToken);
    }

    public async Task<TicketDetailDto> ClassifyAsync(Guid id, ClassifyRequest request, uint? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (ticket, access) = await LoadVisibleAsync(id, cancellationToken, expectedVersion);
        EnsureManage(ticket, access);

        if (request.CategoryId is { } categoryId && await catalog.GetCategoryAsync(categoryId, cancellationToken) is not { IsActive: true })
        {
            throw new BusinessRuleException("category_invalid", "La categoría no existe o está inactiva.");
        }

        var customer = await customers.GetAsync(ticket.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("El ticket referencia un cliente inexistente.");
        var terms = await sla.SelectAsync(PriorityMatrix.Calculate(request.Impact, request.Urgency), customer.ServiceTier, cancellationToken);
        ticket.Reclassify(request.CategoryId, request.Impact, request.Urgency, terms, access.Actor, clock.GetUtcNow());
        return await SaveAndBuildAsync(ticket, access, cancellationToken);
    }

    public async Task<CommentDto> AddCommentAsync(Guid id, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (ticket, access) = await LoadVisibleAsync(id, cancellationToken);
        var terms = await sla.ForPolicyAsync(ticket.SlaPolicyId, cancellationToken);
        var comment = ticket.AddComment(access.UserId, access.Role, request.Body, request.IsInternal, terms, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var name = (await users.GetNamesAsync([access.UserId], cancellationToken)).GetValueOrDefault(access.UserId, "Usuario");
        return new CommentDto(comment.Id, comment.AuthorId, name, access.IsStaff, comment.Body, comment.IsInternal, comment.CreatedAt);
    }

    public async Task<TicketDetailDto> SubmitSurveyAsync(Guid id, SurveyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (ticket, access) = await LoadVisibleAsync(id, cancellationToken);
        if (access.Role != ActorRole.Customer)
        {
            throw new ForbiddenException("Solo el cliente califica la atención.");
        }

        ticket.SubmitSurvey(access.UserId, request.Rating, request.Comment, clock.GetUtcNow());
        return await SaveAndBuildAsync(ticket, access, cancellationToken);
    }

    internal async Task<(Ticket Ticket, AccessContext Access)> LoadVisibleAsync(Guid id, CancellationToken cancellationToken, uint? expectedVersion = null)
    {
        var access = await currentUser.GetAccessAsync(cancellationToken);
        var ticket = await tickets.GetAsync(id, includeDetails: true, cancellationToken);
        if (ticket is null || !TicketAccessPolicy.CanView(ticket, access))
        {
            throw new NotFoundException("Ticket");
        }

        if (expectedVersion is { } version)
        {
            unitOfWork.ExpectVersion(ticket, version);
        }

        return (ticket, access);
    }

    private async Task<TicketDetailDto> SaveAndBuildAsync(Ticket ticket, AccessContext access, CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await details.BuildAsync(ticket, access, cancellationToken);
    }

    private static Guid ResolveCustomer(CreateTicketRequest request, AccessContext access)
    {
        if (access.Role == ActorRole.Customer)
        {
            var own = access.CustomerId ?? throw new ForbiddenException("El usuario no está vinculado a un cliente.");
            if (request.CustomerId is { } requested && requested != own)
            {
                throw new ForbiddenException("No puedes abrir tickets a nombre de otro cliente.");
            }

            return own;
        }

        if (!access.IsStaff)
        {
            throw new ForbiddenException("Rol sin permiso para abrir tickets.");
        }

        return request.CustomerId ?? throw new BusinessRuleException("customer_required", "Indica el cliente del ticket.");
    }

    private static void EnsureManage(Ticket ticket, AccessContext access)
    {
        if (!TicketAccessPolicy.CanManage(ticket, access))
        {
            throw new ForbiddenException("Solo la cola del ticket, su asignado o un supervisor pueden gestionarlo.");
        }
    }

    private static void EnsureCanTransition(Ticket ticket, AccessContext access, TransitionAction action)
    {
        var allowed = action switch
        {
            TransitionAction.Start or TransitionAction.Hold or TransitionAction.Resume or TransitionAction.Resolve => TicketAccessPolicy.CanWork(ticket, access),
            TransitionAction.Reopen => access.Role == ActorRole.Customer || TicketAccessPolicy.CanWork(ticket, access),
            TransitionAction.Close or TransitionAction.Cancel => access.Role == ActorRole.Customer || access.SeesEverything,
            _ => false,
        };

        if (!allowed)
        {
            throw new ForbiddenException("No tienes permiso para esta acción sobre el ticket.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ticket {TicketId} creado con prioridad {Priority}")]
    private static partial void LogTicketCreated(ILogger logger, Guid ticketId, Priority priority);
}
