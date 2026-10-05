using System.Security.Cryptography;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Exceptions;
using Helpdesk.Application.Security;
using Helpdesk.Application.Tickets;
using Helpdesk.Contracts.Tickets;
using Helpdesk.Domain.Attachments;
using Helpdesk.Domain.Tickets;
using Microsoft.Extensions.Logging;

namespace Helpdesk.Application.Attachments;

public sealed record AttachmentContent(Stream Content, string ContentType, string FileName);

/// <summary>
/// Adjuntos (R10): se valida tipo por firma, tamaño y nombre; el archivo se guarda fuera de la base con un
/// nombre aleatorio y solo se descarga pasando por la autorización del ticket.
/// </summary>
public sealed partial class AttachmentService(
    ITicketRepository tickets,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TicketDetailsBuilder details,
    TimeProvider clock,
    ILogger<AttachmentService> logger)
{
    public async Task<AttachmentDto> UploadAsync(Guid ticketId, string? fileName, Stream content, bool isInternal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var access = await currentUser.GetAccessAsync(cancellationToken);
        var ticket = await tickets.GetAsync(ticketId, includeDetails: true, cancellationToken);
        if (ticket is null || !TicketAccessPolicy.CanView(ticket, access))
        {
            throw new NotFoundException("Ticket");
        }

        if (isInternal && !access.IsStaff)
        {
            throw new ForbiddenException("Los clientes no suben adjuntos internos.");
        }

        var bytes = await ReadLimitedAsync(content, cancellationToken);
        var inspection = AttachmentRules.Inspect(fileName, bytes);
        if (!inspection.IsValid)
        {
            LogRejected(logger, ticketId, inspection.ErrorCode);
            throw new BusinessRuleException(inspection.ErrorCode!, inspection.ErrorMessage!);
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var storageKey = await storage.SaveAsync(bytes, cancellationToken);
        try
        {
            var attachment = new TicketAttachment(ticket.Id, null, access.UserId, inspection.SafeFileName, inspection.ContentType!, bytes.Length, sha256, storageKey, isInternal, clock.GetUtcNow());
            ticket.AddAttachment(attachment, access.Role);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            var lookups = await details.LookupsAsync(ticket, cancellationToken);
            return TicketMapper.ToDto(attachment, lookups);
        }
        catch
        {
            // Compensación: si no quedó registrado en la base, el archivo no debe quedar huérfano.
            await storage.DeleteAsync(storageKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<AttachmentContent> DownloadAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        var access = await currentUser.GetAccessAsync(cancellationToken);
        var ticket = await tickets.GetByAttachmentAsync(attachmentId, cancellationToken);
        var attachment = ticket?.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (ticket is null || attachment is null || !TicketAccessPolicy.CanView(ticket, access) || (attachment.IsInternal && !access.IsStaff))
        {
            throw new NotFoundException("Adjunto");
        }

        var stream = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        return new AttachmentContent(stream, attachment.ContentType, attachment.OriginalFileName);
    }

    /// <summary>Lee como máximo el límite + 1 byte: un archivo enorme se rechaza sin cargarlo entero en memoria.</summary>
    private static async Task<byte[]> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > AttachmentRules.MaxBytes)
            {
                throw new BusinessRuleException("attachment_too_large", $"El archivo supera el máximo de {AttachmentRules.MaxBytes / (1024 * 1024)} MB.");
            }
        }

        return buffer.ToArray();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Adjunto rechazado en el ticket {TicketId}: {Reason}")]
    private static partial void LogRejected(ILogger logger, Guid ticketId, string? reason);
}
