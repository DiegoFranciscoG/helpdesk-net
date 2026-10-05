using Helpdesk.Domain.Common;

namespace Helpdesk.Domain.Tickets;

/// <summary>Comentario público (lo ve el cliente) o interno (solo personal).</summary>
public sealed class TicketComment : Entity
{
    public const int BodyMaxLength = 5000;

    private TicketComment()
    {
        Body = string.Empty;
    }

    internal TicketComment(Guid ticketId, Guid authorId, string body, bool isInternal, DateTimeOffset createdAt)
    {
        TicketId = ticketId;
        AuthorId = authorId;
        Body = body;
        IsInternal = isInternal;
        CreatedAt = createdAt;
    }

    public Guid TicketId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; }

    public bool IsInternal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Metadatos de un adjunto; el contenido vive fuera de la base, bajo <see cref="StorageKey"/>.</summary>
public sealed class TicketAttachment : Entity
{
    private TicketAttachment()
    {
        OriginalFileName = string.Empty;
        ContentType = string.Empty;
        Sha256 = string.Empty;
        StorageKey = string.Empty;
    }

    public TicketAttachment(
        Guid ticketId,
        Guid? commentId,
        Guid uploadedById,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string sha256,
        string storageKey,
        bool isInternal,
        DateTimeOffset createdAt)
    {
        TicketId = ticketId;
        CommentId = commentId;
        UploadedById = uploadedById;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StorageKey = storageKey;
        IsInternal = isInternal;
        CreatedAt = createdAt;
    }

    public Guid TicketId { get; private set; }

    public Guid? CommentId { get; private set; }

    public Guid UploadedById { get; private set; }

    public string OriginalFileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; }

    public string StorageKey { get; private set; }

    public bool IsInternal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Registro inmutable de cada cambio de estado (historial_estados).</summary>
public sealed class TicketStatusChange : Entity
{
    private TicketStatusChange()
    {
    }

    internal TicketStatusChange(Guid ticketId, TicketStatus? fromStatus, TicketStatus toStatus, Guid? changedById, string? reason, DateTimeOffset changedAt)
    {
        TicketId = ticketId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedById = changedById;
        Reason = reason;
        ChangedAt = changedAt;
    }

    public Guid TicketId { get; private set; }

    public TicketStatus? FromStatus { get; private set; }

    public TicketStatus ToStatus { get; private set; }

    public Guid? ChangedById { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }
}

/// <summary>Encuesta de satisfacción 1–5, una por ticket (R14).</summary>
public sealed class SatisfactionSurvey : Entity
{
    public const int CommentMaxLength = 1000;

    private SatisfactionSurvey()
    {
    }

    internal SatisfactionSurvey(Guid ticketId, int rating, string? comment, Guid submittedById, DateTimeOffset submittedAt)
    {
        TicketId = ticketId;
        Rating = rating;
        Comment = comment;
        SubmittedById = submittedById;
        SubmittedAt = submittedAt;
    }

    public Guid TicketId { get; private set; }

    public int Rating { get; private set; }

    public string? Comment { get; private set; }

    public Guid SubmittedById { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public bool IsSatisfied => Rating >= 4;
}
