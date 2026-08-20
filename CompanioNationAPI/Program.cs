using CompanioNationAPI;
using CompanioNation.Shared;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Components.WebAssembly.Server;
using Microsoft.Extensions.DependencyInjection;
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
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = SupportedLanguages.Codes.Select(c => new CultureInfo(c)).ToArray();
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;

    var queryProvider = options.RequestCultureProviders.OfType<QueryStringRequestCultureProvider>().FirstOrDefault();
    if (queryProvider is not null)
    {
        queryProvider.QueryStringKey = "lang";
        queryProvider.UIQueryStringKey = "lang";
    }

    var cookieProvider = options.RequestCultureProviders.OfType<CookieRequestCultureProvider>().FirstOrDefault();
    if (cookieProvider is not null)
    {
        cookieProvider.CookieName = "blazorCulture";
    }
});
builder.Services.AddScoped<CompanioNationSignalRClient>();
builder.Services.AddScoped<CultureService>();
builder.Services.AddHttpClient(); // For SSR prerendering HTTP calls to local API endpoints
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient());

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

app.UseRequestLocalization();

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

// Consolidate duplicate SEO URLs (parity with the Services host): English is served at the
// bare URL (no ?lang=) and ?page=1 equals the first page without a page parameter. 301 both
// to the canonical equivalent so Google stops flagging them as "Alternate page with proper
// canonical tag". Skip API endpoints (they use ?lang= as a real parameter) and static assets.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (HttpMethods.IsGet(ctx.Request.Method) &&
        !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) &&
        !Path.HasExtension(path))
    {
        var queryString = ctx.Request.QueryString.Value ?? "";
        if (queryString.Length > 0)
        {
            var kept = queryString.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(IsMeaningfulQueryPair)
                .ToArray();
            var canonicalQuery = kept.Length == 0 ? "" : "?" + string.Join("&", kept);

            if (!string.Equals(canonicalQuery, queryString, StringComparison.Ordinal))
            {
                ctx.Response.Redirect(path + canonicalQuery, permanent: true); // 301
                return;
            }
        }
    }

    await next();
});

// A query pair is meaningful for SEO when it is not the redundant ?lang=en (English is
// served at the bare URL) and not ?page=1 (identical to the first page without a parameter).
static bool IsMeaningfulQueryPair(string pair)
{
    var kv = pair.Split('=', 2);
    if (kv.Length != 2) return true;

    if (kv[0].Equals("lang", StringComparison.OrdinalIgnoreCase) &&
        SupportedLanguages.Normalize(Uri.UnescapeDataString(kv[1])) == "en")
        return false;

    if (kv[0].Equals("page", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(kv[1], out var page) && page == 1)
        return false;

    return true;
}

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

if (isDev)
{
    // Dev-only marker endpoint. The Blazor WASM client probes this on startup to verify it
    // is being served by THIS API host (which maps the SignalR hub) and not the standalone
    // CompanioNationPWA dev server. Both bind to https://localhost:7114, so this is the only
    // reliable way to tell them apart. See CompanioNationPWA/Program.cs startup guard.
    app.MapGet("/_devhost", () => Results.Text("CompanioNationAPI", "text/plain"));
}

// CompanioNita advice REST endpoints — used during SSR prerendering so bot-facing
// URLs (/CompanioNitasCorner/{id}) can render full advice content without SignalR.
app.MapGet("/api/companionita-advice/{adviceId:int}", async (int adviceId, Database db, string? lang = null) =>
{
    string languageCode = SupportedLanguages.Normalize(lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    var result = await db.GetCompanitaAdvice(adviceId, languageCode);
    return result.IsSuccess && result.Data != null
        ? Results.Ok(result.Data)
        : Results.NotFound();
});

app.MapGet("/api/companionita-advice", async (int start, int count, Database db, string? lang = null) =>
{
    string languageCode = SupportedLanguages.Normalize(lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    var result = await db.GetCompanitaAdvice(start, count, languageCode);
    return result.IsSuccess
        ? Results.Ok(result.Data)
        : Results.Problem("Unable to retrieve advice.");
});

// Settings REST endpoint — used during SSR prerendering so the homepage
// AdviceOfTheDay component can render the daily advice without SignalR.
app.MapGet("/api/settings", async (Database db, string? lang = null) =>
{
    string languageCode = SupportedLanguages.Normalize(lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    var settings = await db.GetAllSettingsAsync(languageCode);
    if (settings == null)
        return Results.Problem("Unable to retrieve settings.");

    // Only expose the daily advice field (not internal settings like LastMaintenanceRun)
    return Results.Ok(new Settings { DailyAdvice = settings.DailyAdvice });
});

// Map Blazor Web App with Interactive WebAssembly rendering.
// Server-renders (SSR) the initial HTML so search engines can index page content,
// then the WASM runtime boots in the background and takes over for interactivity.
app.MapRazorComponents<CompanioNationPWA.App>()
    .AddInteractiveWebAssemblyRenderMode();

app.Run();

