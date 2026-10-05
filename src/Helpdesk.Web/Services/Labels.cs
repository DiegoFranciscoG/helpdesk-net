using System.Globalization;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Tickets;
using MudBlazor;

namespace Helpdesk.Web.Services;

/// <summary>Textos en español y colores para estados, prioridades y SLA (el código usa nombres en inglés).</summary>
public static class Labels
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-EC");

    public static string Status(TicketStatus status) => status switch
    {
        TicketStatus.New => "Nuevo",
        TicketStatus.Assigned => "Asignado",
        TicketStatus.InProgress => "En proceso",
        TicketStatus.OnHold => "En espera",
        TicketStatus.Resolved => "Resuelto",
        TicketStatus.Closed => "Cerrado",
        _ => "Cancelado",
    };

    public static Color StatusColor(TicketStatus status) => status switch
    {
        TicketStatus.New => Color.Info,
        TicketStatus.Assigned => Color.Primary,
        TicketStatus.InProgress => Color.Secondary,
        TicketStatus.OnHold => Color.Warning,
        TicketStatus.Resolved => Color.Success,
        _ => Color.Default,
    };

    public static string Priority(Priority priority) => priority switch
    {
        Domain.Tickets.Priority.Critical => "P1 Crítica",
        Domain.Tickets.Priority.High => "P2 Alta",
        Domain.Tickets.Priority.Medium => "P3 Media",
        Domain.Tickets.Priority.Low => "P4 Baja",
        _ => "P5 Planificación",
    };

    public static Color PriorityColor(Priority priority) => priority switch
    {
        Domain.Tickets.Priority.Critical => Color.Error,
        Domain.Tickets.Priority.High => Color.Warning,
        Domain.Tickets.Priority.Medium => Color.Info,
        _ => Color.Default,
    };

    public static string Impact(Impact impact) => impact switch
    {
        Domain.Tickets.Impact.High => "Alto: afecta a muchos clientes o a un proceso crítico",
        Domain.Tickets.Impact.Medium => "Medio: afecta a un grupo o a un servicio importante",
        _ => "Bajo: afecta a una sola persona",
    };

    public static string Urgency(Urgency urgency) => urgency switch
    {
        Domain.Tickets.Urgency.High => "Alta: hay pérdida económica o de seguridad en curso",
        Domain.Tickets.Urgency.Medium => "Media: necesito solución hoy",
        _ => "Baja: puede esperar",
    };

    public static string Level(Impact impact) => impact switch
    {
        Domain.Tickets.Impact.High => "Alto",
        Domain.Tickets.Impact.Medium => "Medio",
        _ => "Bajo",
    };

    public static string Level(Urgency urgency) => urgency switch
    {
        Domain.Tickets.Urgency.High => "Alta",
        Domain.Tickets.Urgency.Medium => "Media",
        _ => "Baja",
    };

    public static string Channel(TicketChannel channel) => channel switch
    {
        TicketChannel.Portal => "Portal",
        TicketChannel.Email => "Correo",
        TicketChannel.Phone => "Teléfono",
        TicketChannel.Chat => "Chat",
        _ => "Agencia",
    };

    public static string Sla(SlaState state) => state switch
    {
        SlaState.OnTrack => "A tiempo",
        SlaState.AtRisk => "En riesgo",
        SlaState.Breached => "Incumplido",
        SlaState.Paused => "Pausado",
        SlaState.Met => "Cumplido",
        _ => "No aplica",
    };

    public static Color SlaColor(SlaState state) => state switch
    {
        SlaState.OnTrack or SlaState.Met => Color.Success,
        SlaState.AtRisk => Color.Warning,
        SlaState.Breached => Color.Error,
        SlaState.Paused => Color.Info,
        _ => Color.Default,
    };

    public static string SlaIcon(SlaState state) => state switch
    {
        SlaState.OnTrack or SlaState.Met => Icons.Material.Filled.CheckCircle,
        SlaState.AtRisk => Icons.Material.Filled.Warning,
        SlaState.Breached => Icons.Material.Filled.Error,
        SlaState.Paused => Icons.Material.Filled.PauseCircle,
        _ => Icons.Material.Filled.RemoveCircleOutline,
    };

    public static string Role(string role) => role switch
    {
        "Admin" => "Administración",
        "Supervisor" => "Supervisión",
        "Agent" => "Agente",
        "Customer" => "Cliente",
        _ => role,
    };

    public static string Tier(ServiceTier tier) => tier == ServiceTier.Premium ? "Premium" : "Estándar";

    public static string Kind(CustomerKind kind) => kind == CustomerKind.Organization ? "Empresa" : "Persona";

    public static string Suggestion(SuggestionStatus status) => status switch
    {
        SuggestionStatus.Pending => "Pendiente de revisión",
        SuggestionStatus.Accepted => "Aceptada",
        SuggestionStatus.Corrected => "Corregida",
        _ => "Descartada",
    };

    public static string NotificationIcon(NotificationType type) => type switch
    {
        NotificationType.SlaBreached => Icons.Material.Filled.Error,
        NotificationType.SlaWarning => Icons.Material.Filled.Warning,
        NotificationType.TicketAssigned => Icons.Material.Filled.AssignmentInd,
        NotificationType.CommentAdded => Icons.Material.Filled.Forum,
        NotificationType.SuggestionReady => Icons.Material.Filled.AutoAwesome,
        NotificationType.StatusChanged => Icons.Material.Filled.SyncAlt,
        _ => Icons.Material.Filled.ConfirmationNumber,
    };

    public static Color NotificationColor(NotificationType type) => type switch
    {
        NotificationType.SlaBreached => Color.Error,
        NotificationType.SlaWarning => Color.Warning,
        NotificationType.SuggestionReady => Color.Secondary,
        _ => Color.Primary,
    };

    public static string Date(DateTimeOffset instant) => instant.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Spanish);

    public static string ShortDate(DateTimeOffset instant) => instant.ToLocalTime().ToString("dd MMM HH:mm", Spanish);

    /// <summary>Vencimiento que manda: el de primera respuesta mientras falte; después, el de resolución.</summary>
    public static DateTimeOffset ActiveDue(TicketListItemDto ticket) =>
        ticket.FirstRespondedAt is null ? ticket.FirstResponseDueAt : ticket.ResolutionDueAt;

    /// <summary>"vence en 2 h 10 min" / "venció hace 35 min".</summary>
    public static string Relative(DateTimeOffset due, DateTimeOffset now)
    {
        var diff = due - now;
        var text = Duration(diff.Duration());
        return diff >= TimeSpan.Zero ? $"vence en {text}" : $"venció hace {text}";
    }

    public static string Duration(TimeSpan span) => span.TotalMinutes < 60
        ? $"{Math.Max(1, (int)span.TotalMinutes)} min"
        : span.TotalHours < 24
            ? $"{(int)span.TotalHours} h {span.Minutes} min"
            : $"{(int)span.TotalDays} d {span.Hours} h";

    /// <summary>Objetivo de SLA: horas de reloj si el calendario es 24×7; si no, horas hábiles y su equivalente en jornadas de 8 h.</summary>
    public static string Target(int minutes, bool is24x7)
    {
        var text = minutes < 60 ? $"{minutes} min" : $"{(minutes / 60.0).ToString("0.#", Spanish)} h";
        if (is24x7)
        {
            return text;
        }

        const int WorkdayMinutes = 8 * 60;
        var days = minutes / WorkdayMinutes;
        return minutes >= WorkdayMinutes && minutes % WorkdayMinutes == 0
            ? $"{text} hábiles ({days} {(days == 1 ? "día" : "días")})"
            : $"{text} hábiles";
    }

    public static string Hours(double? hours) => hours is null ? "—" : $"{hours.Value.ToString("0.#", Spanish)} h";

    public static string Percent(double? value) => value is null ? "—" : $"{value.Value.ToString("0.#", Spanish)} %";

    public static string FileSize(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / (1024.0 * 1024):0.#} MB";
}
