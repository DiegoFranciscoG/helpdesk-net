using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Events;
using Helpdesk.Application.Exceptions;
using Helpdesk.Application.Security;
using Helpdesk.Application.Sla;
using Helpdesk.Contracts.Notifications;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Catalog;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Tickets;
using Microsoft.Extensions.Logging;

namespace Helpdesk.Application.Suggestions;

/// <summary>
/// Sugerencia automática de categoría, equipo y urgencia con el clasificador de P27. El agente siempre decide:
/// acepta, corrige o descarta (LOPDP art. 20). Las correcciones vuelven a P27 como retroalimentación.
/// </summary>
public sealed partial class SuggestionService(
    ITicketRepository tickets,
    ISuggestionRepository suggestions,
    ICatalogRepository catalog,
    ICustomerRepository customers,
    IUserDirectory users,
    ISlaTermsProvider sla,
    INlpClassifier classifier,
    ISuggestionInbox queue,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TicketEventDispatcher dispatcher,
    TimeProvider clock,
    ILogger<SuggestionService> logger)
{
    public const string NlpProvider = "nlp-tickets-es";

    public async Task<IReadOnlyList<SuggestionDto>> ListForTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var (ticket, _) = await LoadManageableAsync(ticketId, requireManage: false, cancellationToken);
        var items = await suggestions.ListForTicketAsync(ticket.Id, cancellationToken);
        return await ToDtosAsync(ticket, items, cancellationToken);
    }

    /// <summary>Pide (otra vez) una sugerencia: se procesa en segundo plano y se avisa por SignalR.</summary>
    public async Task RequestAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var (ticket, _) = await LoadManageableAsync(ticketId, requireManage: true, cancellationToken);
        if (!classifier.IsEnabled)
        {
            throw new BusinessRuleException("nlp_disabled", "La integración con el clasificador no está configurada.");
        }

        if (await suggestions.HasPendingAsync(ticket.Id, cancellationToken))
        {
            throw new BusinessRuleException("suggestion_pending", "El ticket ya tiene una sugerencia pendiente de revisar.");
        }

        queue.Enqueue(ticket.Id);
    }

    /// <summary>Lo ejecuta el worker: llama a P27 y guarda la sugerencia. Devuelve false si no se pudo.</summary>
    public async Task<bool> ProcessAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await tickets.GetAsync(ticketId, includeDetails: false, cancellationToken);
        if (ticket is null || ticket.Status.IsTerminal() || await suggestions.HasPendingAsync(ticketId, cancellationToken))
        {
            return false;
        }

        var text = $"{ticket.Title}\n{ticket.Description}";
        var result = await classifier.ClassifyAsync(text[..Math.Min(text.Length, 5000)], ticket.Channel, ticket.DisplayNumber, cancellationToken);
        if (result is null || result.Suggestion is { CategoryCode: null, TeamCode: null, Urgency: null })
        {
            // Sin respuesta o sin ningún valor utilizable (la respuesta del clasificador se valida como no confiable).
            return false;
        }

        await SaveSuggestionAsync(ticket, SuggestionSource.NlpApi, result.Provider, result.ModelVersion, result.PredictionId, result.Suggestion, cancellationToken);
        return true;
    }

    /// <summary>Sugerencia empujada por un cliente de integración autenticado con JWT (scope suggestions:write).</summary>
    public async Task<SuggestionDto> PushAsync(Guid ticketId, PushSuggestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ticket = await tickets.GetAsync(ticketId, includeDetails: false, cancellationToken) ?? throw new NotFoundException("Ticket");
        if (ticket.Status.IsTerminal())
        {
            throw new BusinessRuleException("ticket_closed", "El ticket ya está cerrado.");
        }

        if (await suggestions.HasPendingAsync(ticket.Id, cancellationToken))
        {
            throw new BusinessRuleException("suggestion_pending", "El ticket ya tiene una sugerencia pendiente de revisar.");
        }

        var suggested = new SuggestedClassification(request.CategoryCode, request.TeamCode, request.Urgency, request.CategoryConfidence, request.TeamConfidence, request.Decision, []);
        var suggestion = await SaveSuggestionAsync(ticket, SuggestionSource.Integration, request.Provider, request.ModelVersion, request.ExternalPredictionId, suggested, cancellationToken);
        return (await ToDtosAsync(ticket, [suggestion], cancellationToken))[0];
    }

    public async Task<SuggestionDto> AcceptAsync(Guid suggestionId, CancellationToken cancellationToken)
    {
        var (suggestion, ticket, access) = await LoadForReviewAsync(suggestionId, cancellationToken);
        var (categoryId, teamId) = await ResolveCodesAsync(suggestion.SuggestedCategoryCode, suggestion.SuggestedTeamCode, cancellationToken);
        await ApplyAsync(ticket, access, categoryId, teamId, suggestion.SuggestedUrgency, "Clasificación sugerida aceptada", cancellationToken);
        suggestion.Accept(access.UserId, categoryId, teamId, suggestion.SuggestedUrgency, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogReviewed(logger, suggestion.Id, SuggestionStatus.Accepted);
        return (await ToDtosAsync(ticket, [suggestion], cancellationToken))[0];
    }

    public async Task<SuggestionDto> CorrectAsync(Guid suggestionId, CorrectSuggestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (suggestion, ticket, access) = await LoadForReviewAsync(suggestionId, cancellationToken);
        var categories = await catalog.ListCategoriesAsync(cancellationToken);
        var teams = await catalog.ListTeamsAsync(includeMembers: false, cancellationToken);
        var category = request.CategoryId is { } c ? categories.FirstOrDefault(x => x.Id == c && x.IsActive) ?? throw new BusinessRuleException("category_invalid", "Categoría no válida.") : null;
        var team = request.TeamId is { } t ? teams.FirstOrDefault(x => x.Id == t && x.IsActive) ?? throw new BusinessRuleException("team_invalid", "Cola no válida.") : null;

        await ApplyAsync(ticket, access, category?.Id, team?.Id, request.Urgency, "Clasificación sugerida corregida", cancellationToken);
        suggestion.Correct(access.UserId, category?.Id, team?.Id, request.Urgency, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogReviewed(logger, suggestion.Id, SuggestionStatus.Corrected);

        if (suggestion is { Source: SuggestionSource.NlpApi, ExternalPredictionId: { } predictionId } && classifier.CanSendFeedback)
        {
            try
            {
                await classifier.SendFeedbackAsync(predictionId, category?.Code, team?.Code, request.Urgency, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFeedbackFailed(logger, ex, suggestion.Id);
            }
        }

        return (await ToDtosAsync(ticket, [suggestion], cancellationToken))[0];
    }

    public async Task<SuggestionDto> DismissAsync(Guid suggestionId, CancellationToken cancellationToken)
    {
        var (suggestion, ticket, access) = await LoadForReviewAsync(suggestionId, cancellationToken);
        suggestion.Dismiss(access.UserId, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await ToDtosAsync(ticket, [suggestion], cancellationToken))[0];
    }

    private async Task<TicketSuggestion> SaveSuggestionAsync(
        Ticket ticket,
        SuggestionSource source,
        string provider,
        string modelVersion,
        Guid? predictionId,
        SuggestedClassification suggested,
        CancellationToken cancellationToken)
    {
        var suggestion = new TicketSuggestion(ticket.Id, source, provider, modelVersion, predictionId, suggested, clock.GetUtcNow());
        suggestions.Add(suggestion);

        var recipients = ticket.AssigneeId is { } assignee ? [assignee] : await users.GetTeamLeadIdsAsync(ticket.TeamId, cancellationToken);
        dispatcher.Stage(recipients, ticket, NotificationType.SuggestionReady, $"{ticket.DisplayNumber}: sugerencia lista", "Revisa la categoría y la cola sugeridas.");
        dispatcher.QueueChange(ticket, TicketChangeKind.SuggestionReady);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return suggestion;
    }

    /// <summary>Aplica la clasificación: recalcula prioridad y SLA y, si cambia la cola y el estado lo permite, transfiere.</summary>
    private async Task ApplyAsync(Ticket ticket, AccessContext access, Guid? categoryId, Guid? teamId, Urgency? urgency, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (categoryId is not null || urgency is not null)
        {
            var customer = await customers.GetAsync(ticket.CustomerId, cancellationToken)
                ?? throw new InvalidOperationException("El ticket referencia un cliente inexistente.");
            var newUrgency = urgency ?? ticket.Urgency;
            var terms = await sla.SelectAsync(PriorityMatrix.Calculate(ticket.Impact, newUrgency), customer.ServiceTier, cancellationToken);
            ticket.Reclassify(categoryId ?? ticket.CategoryId, ticket.Impact, newUrgency, terms, access.Actor, now);
        }

        if (teamId is { } team && team != ticket.TeamId && TicketStateMachine.IsPermitted(ticket.Status, TicketTrigger.Transfer, access.Role))
        {
            ticket.Transfer(team, reason, access.Actor, now);
        }
    }

    private async Task<(TicketSuggestion Suggestion, Ticket Ticket, AccessContext Access)> LoadForReviewAsync(Guid suggestionId, CancellationToken cancellationToken)
    {
        var suggestion = await suggestions.GetAsync(suggestionId, cancellationToken) ?? throw new NotFoundException("Sugerencia");
        var (ticket, access) = await LoadManageableAsync(suggestion.TicketId, requireManage: true, cancellationToken);
        if (ticket.Status.IsTerminal() || ticket.Status == TicketStatus.Resolved)
        {
            throw new BusinessRuleException("ticket_not_editable", "Solo se aplican sugerencias a tickets abiertos.");
        }

        return (suggestion, ticket, access);
    }

    private async Task<(Ticket Ticket, AccessContext Access)> LoadManageableAsync(Guid ticketId, bool requireManage, CancellationToken cancellationToken)
    {
        var access = await currentUser.GetAccessAsync(cancellationToken);
        var ticket = await tickets.GetAsync(ticketId, includeDetails: false, cancellationToken);
        if (ticket is null || !access.IsStaff || !TicketAccessPolicy.CanView(ticket, access))
        {
            throw new NotFoundException("Ticket");
        }

        if (requireManage && !TicketAccessPolicy.CanManage(ticket, access))
        {
            throw new ForbiddenException("Solo la cola del ticket, su asignado o un supervisor revisan sugerencias.");
        }

        return (ticket, access);
    }

    private async Task<(Guid? CategoryId, Guid? TeamId)> ResolveCodesAsync(string? categoryCode, string? teamCode, CancellationToken cancellationToken)
    {
        var category = categoryCode is null ? null : (await catalog.ListCategoriesAsync(cancellationToken)).FirstOrDefault(c => c.Code == categoryCode && c.IsActive);
        var team = teamCode is null ? null : (await catalog.ListTeamsAsync(includeMembers: false, cancellationToken)).FirstOrDefault(t => t.Code == teamCode && t.IsActive);
        if (category is null && team is null)
        {
            throw new BusinessRuleException("suggestion_unmapped", "Los códigos sugeridos no existen en el catálogo local: corrige la sugerencia.");
        }

        return (category?.Id, team?.Id);
    }

    private async Task<IReadOnlyList<SuggestionDto>> ToDtosAsync(Ticket ticket, IReadOnlyList<TicketSuggestion> items, CancellationToken cancellationToken)
    {
        IReadOnlyList<Category> categories = await catalog.ListCategoriesAsync(cancellationToken);
        IReadOnlyList<Team> teams = await catalog.ListTeamsAsync(includeMembers: false, cancellationToken);
        var reviewers = await users.GetNamesAsync(items.Where(i => i.ReviewedById is not null).Select(i => i.ReviewedById!.Value), cancellationToken);

        return [.. items.OrderByDescending(i => i.CreatedAt).Select(i =>
        {
            var category = categories.FirstOrDefault(c => c.Code == i.SuggestedCategoryCode);
            var team = teams.FirstOrDefault(t => t.Code == i.SuggestedTeamCode);
            Priority? priority = i.SuggestedUrgency is { } u ? PriorityMatrix.Calculate(ticket.Impact, u) : null;
            return new SuggestionDto(
                i.Id, i.TicketId, i.Status, i.Source, i.Provider, i.ModelVersion,
                i.SuggestedCategoryCode, category?.Id, category?.Name,
                i.SuggestedTeamCode, team?.Id, team?.Name,
                i.SuggestedUrgency, priority, i.CategoryConfidence, i.TeamConfidence, i.Decision, i.ReviewReasons,
                i.CreatedAt, i.ReviewedById is { } r ? reviewers.GetValueOrDefault(r) : null, i.ReviewedAt);
        })];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sugerencia {SuggestionId} revisada: {Status}")]
    private static partial void LogReviewed(ILogger logger, Guid suggestionId, SuggestionStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo enviar la corrección de la sugerencia {SuggestionId} al clasificador")]
    private static partial void LogFeedbackFailed(ILogger logger, Exception exception, Guid suggestionId);
}
