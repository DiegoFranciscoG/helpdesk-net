using Helpdesk.Contracts.Validation;
using Microsoft.AspNetCore.Identity;

namespace Helpdesk.Infrastructure.Identity;

/// <summary>
/// Política NIST SP 800-63B-4: longitud 15–128 sin reglas de composición, rechazo de contraseñas comunes,
/// repetitivas o secuenciales, y de las que contienen el correo o el nombre del usuario.
/// </summary>
internal sealed class CommonPasswordValidator : IPasswordValidator<AppUser>
{
    // Lista corta de patrones frecuentes (la longitud mínima de 15 ya descarta las listas clásicas de 8–12).
    private static readonly HashSet<string> Blocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012345", "1234567890123456", "qwertyuiopasdfg", "qwertyuiopasdfgh", "asdfghjklzxcvbn",
        "contraseña12345", "contrasena12345", "contraseña123456", "contrasena123456", "password1234567",
        "passwordpassword", "administrador123", "administrador1234", "bienvenido12345", "bienvenido2026!",
        "ecuador123456789", "ecuador12345678", "quito1234567890", "guayaquil123456", "cuenca123456789",
        "iloveyou1234567", "abcdefghijklmno", "abc123abc123abc", "helpdesk1234567", "mesadeayuda12345",
    };

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        var value = password ?? string.Empty;
        var errors = new List<IdentityError>();

        if (value.Length > PasswordRules.MaxLength)
        {
            errors.Add(new IdentityError { Code = "PasswordTooLong", Description = $"La contraseña admite máximo {PasswordRules.MaxLength} caracteres." });
        }

        if (Blocklist.Contains(value) || value.Distinct().Count() < 4 || IsSequential(value) || IsRepeatedPattern(value))
        {
            errors.Add(new IdentityError { Code = "PasswordTooCommon", Description = "La contraseña es demasiado común o predecible." });
        }

        var emailLocalPart = user.Email?.Split('@')[0] ?? string.Empty;
        var nameParts = user.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 4);
        if ((emailLocalPart.Length >= 4 && value.Contains(emailLocalPart, StringComparison.OrdinalIgnoreCase))
            || nameParts.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(new IdentityError { Code = "PasswordContainsUserData", Description = "La contraseña no debe contener tu correo ni tu nombre." });
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }

    /// <summary>Secuencias como "abcdef…" o "1234567890123…" (los dígitos se consideran cíclicos: 9 → 0).</summary>
    private static bool IsSequential(string value)
    {
        if (value.Length < 2)
        {
            return false;
        }

        var allDigits = value.All(char.IsAsciiDigit);
        var step = allDigits ? Mod10(value[1] - value[0]) : value[1] - value[0];
        if (allDigits ? step is not (1 or 9) : Math.Abs(step) != 1)
        {
            return false;
        }

        for (var i = 2; i < value.Length; i++)
        {
            var current = allDigits ? Mod10(value[i] - value[i - 1]) : value[i] - value[i - 1];
            if (current != step)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Patrones cortos repetidos ("abcabcabc…", "12121212…").</summary>
    private static bool IsRepeatedPattern(string value)
    {
        for (var period = 1; period <= Math.Min(6, value.Length / 3); period++)
        {
            var repeated = true;
            for (var i = period; i < value.Length && repeated; i++)
            {
                repeated = value[i] == value[i % period];
            }

            if (repeated)
            {
                return true;
            }
        }

        return false;
    }

    private static int Mod10(int value) => ((value % 10) + 10) % 10;
}

/// <summary>Mensajes de Identity en español.</summary>
internal sealed class SpanishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = "Ya existe un usuario con ese correo." };

    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = "Ya existe un usuario con ese correo." };

    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "El correo no es válido." };

    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"La contraseña debe tener al menos {length} caracteres." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"La contraseña necesita al menos {uniqueChars} caracteres distintos." };

    public override IdentityError InvalidRoleName(string? role) => new() { Code = nameof(InvalidRoleName), Description = "Rol no válido." };
}
