using System.Globalization;
using Helpdesk.Web;
using Helpdesk.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var spanish = CultureInfo.GetCultureInfo("es-EC");
CultureInfo.DefaultThreadCurrentCulture = spanish;
CultureInfo.DefaultThreadCurrentUICulture = spanish;

// La URL de la API viene de wwwroot/appsettings.json (el contenedor la escribe desde una variable de entorno).
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));
var apiBase = new Uri((builder.Configuration["Api:BaseUrl"] ?? builder.HostEnvironment.BaseAddress).TrimEnd('/') + "/");

builder.Services.AddScoped<SessionService>();
builder.Services.AddTransient<AuthorizingHandler>();
builder.Services.AddHttpClient(SessionService.AnonymousClient, client => client.BaseAddress = apiBase);
builder.Services.AddHttpClient<HelpdeskApi>(client => client.BaseAddress = apiBase).AddHttpMessageHandler<AuthorizingHandler>();
builder.Services.AddScoped<RealtimeClient>();

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<HelpdeskAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<HelpdeskAuthStateProvider>());

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.VisibleStateDuration = 5000;
    config.SnackbarConfiguration.PreventDuplicates = true;
});

var host = builder.Build();
await host.Services.GetRequiredService<SessionService>().RestoreAsync();
await host.RunAsync();
