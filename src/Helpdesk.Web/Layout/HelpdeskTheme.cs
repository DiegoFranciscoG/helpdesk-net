using MudBlazor;

namespace Helpdesk.Web.Layout;

/// <summary>Tema propio con colores que cumplen contraste AA sobre fondo blanco.</summary>
public static class HelpdeskTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#0f5c6e",
            Secondary = "#9a3412",
            Tertiary = "#5b21b6",
            Info = "#1557a0",
            Success = "#1b6e2d",
            Warning = "#8a4b00",
            Error = "#b3261e",
            AppbarBackground = "#0f5c6e",
            Background = "#f5f7f8",
            DrawerBackground = "#ffffff",
            TextPrimary = "#1f2933",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5fb3c6",
            Secondary = "#fdba74",
            AppbarBackground = "#0b3d49",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["system-ui", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"] },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "8px" },
    };
}
