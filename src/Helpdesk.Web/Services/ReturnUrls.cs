namespace Helpdesk.Web.Services;

/// <summary>Valida el <c>returnUrl</c> del login para evitar redirecciones abiertas (OWASP Unvalidated Redirects).</summary>
public static class ReturnUrls
{
    /// <summary>
    /// Solo rutas internas relativas. Se rechaza todo lo que un navegador podría interpretar como otro origen:
    /// esquemas (<c>https:</c>, <c>javascript:</c>), URLs sin esquema (<c>//host</c>) y barras invertidas
    /// (<c>/\host</c>, que los navegadores normalizan a <c>//host</c>).
    /// </summary>
    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return string.Empty;
        }

        var value = returnUrl.Trim();
        var external = value.Contains("//", StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || value.Any(char.IsControl);
        return external ? string.Empty : value.TrimStart('/');
    }
}
