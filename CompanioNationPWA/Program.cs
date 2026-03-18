using CompanioNation.Shared;
using CompanioNationPWA;
using CompanioNationPWA.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;


var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register the CompanioNationSignalRClient as a singleton
builder.Services.AddSingleton<CompanioNationSignalRClient>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddSingleton<CultureService>();

var host = builder.Build();

// Initialize culture from localStorage or browser auto-detection before rendering
var cultureService = host.Services.GetRequiredService<CultureService>();
await cultureService.InitializeCultureAsync();

await host.RunAsync();
