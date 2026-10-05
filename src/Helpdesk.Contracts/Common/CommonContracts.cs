namespace Helpdesk.Contracts.Common;

/// <summary>Página de resultados.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Constantes compartidas por la API y el front.</summary>
public static class ApiRoutes
{
    public const string Base = "api/v1";
    public const string Hub = "hubs/notifications";
}

/// <summary>Versión vigente del aviso de privacidad que aceptan los clientes (LOPDP arts. 10 c y 12).</summary>
public static class PrivacyNotice
{
    public const string CurrentVersion = "2026-10";
}
