using CompanioNationAPI;
using CompanioNation.Shared;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Components.WebAssembly.Server;

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

var app = builder.Build();

var GtmId = app.Configuration["GTM_ID"] ?? "";

// Cache index.html with GTM injected at startup (placeholder pattern; avoids hardcoding the ID in a static file)
var indexHtml = "";
var indexFileInfo = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
if (indexFileInfo.Exists)
{
    using var stream = indexFileInfo.CreateReadStream();
    using var reader = new StreamReader(stream);
    indexHtml = reader.ReadToEnd()
        .Replace("<!--GTM_HEAD-->", Util.GtmHeadScript(GtmId))
        .Replace("<!--GTM_BODY-->", Util.GtmBodyNoscript(GtmId));
}

// Configure the HTTP request pipeline.
if (!isDev)
{
    app.UseExceptionHandler("/Error");
    // TODO - maybe use HSTS in future?
    //app.UseHsts();
}
else
{
    app.UseWebAssemblyDebugging();
}

app.UseHttpsRedirection();

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
var hub = app.MapHub<CompanioNationHub>("/CompanioNationHub");

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

// Fallback to index.html for Blazor WASM client-side routing
// This must come AFTER all other specific route mappings
app.MapFallback(ctx =>
{
    ctx.Response.ContentType = "text/html; charset=utf-8";
    return ctx.Response.WriteAsync(indexHtml);
});

app.Run();

