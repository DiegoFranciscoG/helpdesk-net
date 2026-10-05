using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Tickets;

namespace Helpdesk.Application.Tickets;

/// <summary>Semáforo del SLA calculado en el instante de la consulta (no depende de que haya corrido el monitor).</summary>
public static class SlaStateCalculator
{
    public static SlaState Calculate(Ticket ticket, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return Calculate(
            ticket.Status,
            ticket.FirstRespondedAt,
            ticket.FirstResponseWarningAt,
            ticket.FirstResponseDueAt,
            ticket.FirstResponseBreached,
            ticket.ResolutionWarningAt,
            ticket.ResolutionDueAt,
            ticket.ResolutionBreached,
            now);
    }

    public static SlaState Calculate(
        TicketStatus status,
        DateTimeOffset? firstRespondedAt,
        DateTimeOffset firstResponseWarningAt,
        DateTimeOffset firstResponseDueAt,
        bool firstResponseBreached,
        DateTimeOffset resolutionWarningAt,
        DateTimeOffset resolutionDueAt,
        bool resolutionBreached,
        DateTimeOffset now)
    {
        if (status == TicketStatus.Cancelled)
        {
            return SlaState.NotApplicable;
        }

        var breached = firstResponseBreached || resolutionBreached;
        if (status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return breached ? SlaState.Breached : SlaState.Met;
        }

        var awaitingResponse = firstRespondedAt is null;
        var running = status.IsResolutionClockRunning();
        breached |= (awaitingResponse && now > firstResponseDueAt) || (running && now > resolutionDueAt);
        if (breached)
        {
            return SlaState.Breached;
        }

        if (status == TicketStatus.OnHold)
        {
            return SlaState.Paused;
        }

        var atRisk = (awaitingResponse && now >= firstResponseWarningAt) || (running && now >= resolutionWarningAt);
        return atRisk ? SlaState.AtRisk : SlaState.OnTrack;
    }
}
