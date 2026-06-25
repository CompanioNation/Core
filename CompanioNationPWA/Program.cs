using CompanioNation.Shared;
using CompanioNationPWA;
using CompanioNationPWA.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;


var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register the CompanioNationSignalRClient as a singleton
builder.Services.AddSingleton<CompanioNationSignalRClient>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddSingleton<CultureService>();

// Forward framework-level Error/Critical logs to the server via SignalR.
// This catches unhandled exceptions that show #blazor-error-ui but bypass ErrorBoundary.
builder.Services.AddSingleton<ILoggerProvider, SignalRLoggerProvider>();

var host = builder.Build();

// Development guard: ensure we are served by a host that maps the SignalR hub
// (CompanioNationAPI or the full CompanioNationServices host), not the standalone
// CompanioNationPWA dev server. They all bind to the same URL, so we probe a dev-only marker
// endpoint. If it is missing, show a clear overlay and stop instead of letting the app fail
// later with a 405 on /CompanioNationHub/negotiate.
if (builder.HostEnvironment.IsDevelopment()
    && !await IsServedByHubHostAsync(builder.HostEnvironment.BaseAddress))
{
    await host.Services.GetRequiredService<IJSRuntime>().InvokeVoidAsync("cnShowStartupGuard");
    return;
}

// Initialize culture from localStorage or browser auto-detection before rendering
var cultureService = host.Services.GetRequiredService<CultureService>();
await cultureService.InitializeCultureAsync();

await host.RunAsync();

static async Task<bool> IsServedByHubHostAsync(string baseAddress)
{
    // Hosts that map /CompanioNationHub expose a dev-only /_devhost marker identifying themselves.
    // The standalone CompanioNationPWA dev server has no such endpoint (it returns index.html),
    // so any value outside this set means we are running the client without a hub host.
    string[] knownHubHosts = ["CompanioNationAPI", "CompanioNationServices"];
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(5) };
        using var response = await http.GetAsync("_devhost");
        if (!response.IsSuccessStatusCode)
            return false;

        var body = (await response.Content.ReadAsStringAsync()).Trim();
        return knownHubHosts.Contains(body, StringComparer.Ordinal);
    }
    catch
    {
        // Network failure or an unexpected (non-marker) response means we are not being
        // served by a hub host, so treat it as a standalone launch.
        return false;
    }
}
