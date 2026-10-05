using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Helpdesk.Api.Infrastructure;

/// <summary>Ejecuta el validador FluentValidation de cada argumento antes de la acción (las mismas reglas que usa el front).</summary>
internal sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var argument in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is IValidator validator)
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        await next();
    }
}

/// <summary>Utilidades comunes a los controladores: ETag e If-Match para concurrencia optimista (RFC 9110 §13.1.1).</summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    protected uint? IfMatchVersion()
    {
        var header = Request.Headers.IfMatch.ToString().Trim();
        if (string.IsNullOrEmpty(header) || header == "*")
        {
            return null;
        }

        var value = header.StartsWith("W/", StringComparison.Ordinal) ? header[2..] : header;
        return uint.TryParse(value.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : throw new BadHttpRequestException("Cabecera If-Match inválida.");
    }

    protected void SetETag(uint version) => Response.Headers.ETag = $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";
}

/// <summary>Declara el esquema Bearer (JWT) en el documento OpenAPI para probar la API desde /docs.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Info = new OpenApiInfo
        {
            Title = "helpdesk-net API",
            Version = "v1",
            Description = "Mesa de ayuda con SLA sobre calendario laboral de Ecuador, roles, adjuntos, tiempo real y sugerencias de IA (P27). Autenticación JWT Bearer.",
            License = new OpenApiLicense { Name = "MIT" },
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token obtenido en POST /api/v1/auth/login (usuarios) o POST /api/v1/auth/token (clientes de integración).",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}

/// <summary>
/// <c>dotnet Helpdesk.Api.dll --healthcheck</c>: la imagen chiseled no trae curl ni shell, así que el healthcheck
/// del contenedor consulta <c>/health/live</c> con el propio runtime.
/// </summary>
internal static class HealthCheckCommand
{
    public static async Task<int> RunAsync()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';', ',')[0] ?? "8080";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health/live"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}

/// <summary>Acepta enumeraciones en snake_case también en query string y rutas (<c>?sort=due_date</c>), igual que en JSON.</summary>
internal sealed class SnakeCaseEnumModelBinderProvider : Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinderProvider
{
    public Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinder? GetBinder(Microsoft.AspNetCore.Mvc.ModelBinding.ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return type.IsEnum && context.BindingInfo.BindingSource != Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Body
            ? new SnakeCaseEnumModelBinder(type)
            : null;
    }

    private sealed class SnakeCaseEnumModelBinder(Type enumType) : Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinder
    {
        public Task BindModelAsync(Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingContext bindingContext)
        {
            ArgumentNullException.ThrowIfNull(bindingContext);
            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
            if (string.IsNullOrEmpty(value))
            {
                return Task.CompletedTask;
            }

            var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal);
            if (!normalized.All(char.IsDigit) && Enum.TryParse(enumType, normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(enumType, parsed))
            {
                bindingContext.Result = Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingResult.Success(parsed);
            }
            else
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"Valor no válido: '{value}'.");
            }

            return Task.CompletedTask;
        }
    }
}
