using FluentValidation;
using Helpdesk.Application.Exceptions;
using Helpdesk.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Helpdesk.Api.Errors;

/// <summary>
/// Traduce excepciones a ProblemDetails (RFC 9457) con un código estable y sin detalles internos ni stack traces
/// (OWASP A10:2025). Los errores inesperados se registran con su traceId para poder rastrearlos.
/// </summary>
internal sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var (status, code, title, detail) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "validation_error", "Datos inválidos", "Revisa los campos marcados."),
            NotFoundException e => (StatusCodes.Status404NotFound, "not_found", "No encontrado", e.Message),
            AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, "authentication_failed", "No autenticado", e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, "forbidden", "Acción no permitida", e.Message),
            ActionNotPermittedException e => (StatusCodes.Status403Forbidden, e.Code, "Acción no permitida", e.Message),
            InvalidTransitionException e => (StatusCodes.Status409Conflict, e.Code, "Transición no válida", e.Message),
            ConcurrencyConflictException e => (StatusCodes.Status409Conflict, "concurrency_conflict", "Conflicto de edición", e.Message),
            PreconditionFailedException e => (StatusCodes.Status412PreconditionFailed, "precondition_failed", "Versión desactualizada", e.Message),
            BusinessRuleException { Code: "attachment_too_large" } e => (StatusCodes.Status413PayloadTooLarge, e.Code, "Archivo demasiado grande", e.Message),
            BusinessRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Code, "Regla de negocio", e.Message),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Code, "Regla de negocio", e.Message),
            BadHttpRequestException e => (e.StatusCode, "bad_request", "Petición inválida", "La petición no se pudo procesar."),
            FileNotFoundException => (StatusCodes.Status404NotFound, "file_missing", "Archivo no disponible", "El archivo del adjunto ya no está disponible."),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "Error interno", "Ocurrió un error inesperado. Usa el traceId para reportarlo."),
        };

        if (status >= 500)
        {
            LogUnhandled(logger, exception, httpContext.TraceIdentifier);
        }

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        if (exception is ValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => JsonName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }

    internal static string JsonName(string propertyName) =>
        string.IsNullOrEmpty(propertyName) ? string.Empty : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];

    [LoggerMessage(Level = LogLevel.Error, Message = "Error no controlado (traceId {TraceId})")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string traceId);
}
