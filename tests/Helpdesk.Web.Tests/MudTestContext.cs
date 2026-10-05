using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Helpdesk.Web.Tests;

/// <summary>Contexto de bUnit con los servicios de MudBlazor; su JavaScript no se ejecuta en las pruebas.</summary>
public abstract class MudTestContext : BunitContext
{
    protected MudTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }
}
