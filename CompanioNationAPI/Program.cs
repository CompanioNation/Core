using CompanioNationAPI;
using CompanioNation.Shared;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Components.WebAssembly.Server;
using System.Threading.RateLimiting;
using CompanioNationPWA;
using CompanioNationPWA.Services;

/*
**** NOTE: ON IIS PRODUCTION SERVER
****        MUST INSTALL WebSocket Protocol on IIS for this to work!!!
****    eg: Enable WebSocket on Azure Services App
 */

var builder = WebApplication.CreateBuilder(args);
var isDev = builder.Environment.IsDevelopment();

if (isDev)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DevCors", p => p
            .WithOrigins("https://localhost:7114")
            .AllowAnyHeader()
            .AllowAnyMethod()
        );
    });

    CoreServiceExtensions.LoadEnvFileIfPresent("myapp.env");
}

// Shared core services (Database, SignalR, push notifications, maintenance, etc.)
builder.Services.AddCompanioNationCore(isDev);

// Blazor Web App with Interactive WebAssembly rendering — enables server-side
// prerendering (SSR) for search engine indexing while keeping full WASM interactivity.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Services the WASM client normally registers itself, but the SSR host also needs
// them to prerender components that inject them.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddScoped<CompanioNationSignalRClient>();
builder.Services.AddScoped<CultureService>();

// HSTS — short max-age initially; raise to 30 days → 1 year once HTTPS is confirmed solid on all hosts
if (!isDev)
{
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromMinutes(5);
        options.IncludeSubDomains = false;
        options.Preload = false;
    });
}

// Rate limiting — protect the SignalR negotiate endpoint from connection flooding
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("negotiate", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

var app = builder.Build();

var GtmId = app.Configuration["GTM_ID"] ?? "";

// Configure the HTTP request pipeline.
if (!isDev)
{
    app.UseExceptionHandler("/Error");
    // HSTS: 5-min max-age while confidence builds; raise to 30d–1yr after confirming zero HTTPS issues.
    app.UseHsts();
}
else
{
    app.UseWebAssemblyDebugging();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAntiforgery();

// Use response compression before static assets (but after HTTPS redirection)
if (!isDev)
    app.UseResponseCompression();

// Static assets with .NET 10 best practices
// MapStaticAssets handles fingerprinting, compression, and caching automatically
app.MapStaticAssets();

if (isDev)
    app.UseCors("DevCors");

// Map endpoints once; apply CORS only in dev
var controllers = app.MapControllers();
var hub = app.MapHub<CompanioNationHub>("/CompanioNationHub").RequireRateLimiting("negotiate");

if (isDev)
{
    controllers.RequireCors("DevCors");
    hub.RequireCors("DevCors");
}

app.MapGet("/Error", (HttpContext ctx) =>
{
    var feature = ctx.Features.Get<IExceptionHandlerFeature>();
    if (feature == null)
    {
        ErrorLog.LogErrorMessage("Unhandled exception occurred, caught in Program.cs, but no exception details are available.");
    }
    else
    {
        ErrorLog.LogErrorException(feature.Error, "Unhandled exception occurred, caught in Program.cs");
    }

    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    ctx.Response.ContentType = "text/html; charset=utf-8";
    return Results.Text(Util.RenderFruitLoopyErrorHtml(GtmId), "text/html; charset=utf-8");
});

// Privacy Policy - server-rendered so bots/crawlers can read it without JavaScript
app.MapPrivacyPolicyEndpoints(GtmId);

if (isDev)
{
    // Dev-only marker endpoint. The Blazor WASM client probes this on startup to verify it
    // is being served by THIS API host (which maps the SignalR hub) and not the standalone
    // CompanioNationPWA dev server. Both bind to https://localhost:7114, so this is the only
    // reliable way to tell them apart. See CompanioNationPWA/Program.cs startup guard.
    app.MapGet("/_devhost", () => Results.Text("CompanioNationAPI", "text/plain"));
}

// Map Blazor Web App with Interactive WebAssembly rendering.
// Server-renders (SSR) the initial HTML so search engines can index page content,
// then the WASM runtime boots in the background and takes over for interactivity.
app.MapRazorComponents<CompanioNationPWA.App>()
    .AddInteractiveWebAssemblyRenderMode();

app.Run();

