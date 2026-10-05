using FluentValidation;

namespace Helpdesk.Web.Components;

/// <summary>
/// Conecta los validadores FluentValidation compartidos con MudForm: el front valida con las mismas reglas
/// que la API (que igual vuelve a validar).
/// </summary>
public static class FormValidation
{
    public static Func<object, string, Task<IEnumerable<string>>> For<T>(IValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        return async (model, propertyName) =>
        {
            var context = ValidationContext<T>.CreateWithOptions((T)model, options => options.IncludeProperties(propertyName));
            var result = await validator.ValidateAsync(context);
            return result.IsValid ? [] : result.Errors.Select(e => e.ErrorMessage);
        };
    }
}
