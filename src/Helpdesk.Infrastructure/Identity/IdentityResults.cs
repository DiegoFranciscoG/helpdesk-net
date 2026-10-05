using Helpdesk.Application.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Helpdesk.Infrastructure.Identity;

internal static class IdentityResults
{
    /// <summary>
    /// UserManager no lanza excepciones: devuelve el error. Ignorarlo dejaría la entidad modificada en el
    /// DbContext y el siguiente SaveChanges fallaría con un 500; aquí se traduce a un 409 explícito.
    /// </summary>
    public static void EnsureSucceeded(this IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
        {
            throw new ConcurrencyConflictException("El usuario fue modificado por otra operación. Vuelve a intentarlo.");
        }

        throw new BusinessRuleException("user_rejected", string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
