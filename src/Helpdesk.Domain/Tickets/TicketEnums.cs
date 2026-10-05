namespace Helpdesk.Domain.Tickets;

/// <summary>Estados del ciclo de vida del incidente (R5).</summary>
public enum TicketStatus
{
    New,
    Assigned,
    InProgress,
    OnHold,
    Resolved,
    Closed,
    Cancelled,
}

/// <summary>Impacto ITIL: alcance del daño. 1 = alto.</summary>
public enum Impact
{
    High = 1,
    Medium = 2,
    Low = 3,
}

/// <summary>Urgencia ITIL: rapidez con que se necesita la solución. 1 = alta.</summary>
public enum Urgency
{
    High = 1,
    Medium = 2,
    Low = 3,
}

/// <summary>Prioridad ITIL calculada desde impacto y urgencia (R1).</summary>
public enum Priority
{
    Critical = 1,
    High = 2,
    Medium = 3,
    Low = 4,
    Planning = 5,
}

/// <summary>Canal por el que llegó el ticket.</summary>
public enum TicketChannel
{
    Portal,
    Email,
    Phone,
    Chat,
    WalkIn,
}

/// <summary>Acciones que disparan la máquina de estados.</summary>
public enum TicketTrigger
{
    Assign,
    Start,
    Hold,
    Resume,
    Resolve,
    Reopen,
    Close,
    Cancel,
    Transfer,
}

public static class TicketStatusExtensions
{
    public static bool IsTerminal(this TicketStatus status) => status is TicketStatus.Closed or TicketStatus.Cancelled;

    /// <summary>El reloj de resolución corre solo en estos estados (en espera y resuelto lo pausan).</summary>
    public static bool IsResolutionClockRunning(this TicketStatus status) =>
        status is TicketStatus.New or TicketStatus.Assigned or TicketStatus.InProgress;
}
