using CompanioNationAPI;
using CompanioNation.Shared;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Components.WebAssembly.Server;

/*
**** NOTE: ON IIS PRODUCTION SERVER
****        MUST INSTALL WebSocket Protocol on IIS for this to work!!!
****    eg: Enable WebSocket on Azure Services App
 */

static void LoadEnvFileIfPresent(string path)
{
    string d = Directory.GetCurrentDirectory();
    if (!File.Exists(path)) return;

    foreach (var raw in File.ReadAllLines(path))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#")) continue;
        var idx = line.IndexOf('=');
        if (idx <= 0) continue;

        var key = line[..idx].Trim();
        var val = line[(idx + 1)..].Trim();
        Environment.SetEnvironmentVariable(key, val); // process scope
    }
}

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

    LoadEnvFileIfPresent("myapp.env");
}

// Core services
builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB, which is more than enough for 42 KB
});

builder.Services.AddSingleton<CompanioNita>();
builder.Services.AddSingleton<Database>();

// Background maintenance (stub-safe)
builder.Services.AddSingleton<MaintenanceEventService>();
builder.Services.AddHostedService<MaintenanceEventService>();

// Response compression: only enable outside dev (Hot Reload friendliness)
// NOTE: MapStaticAssets() handles its own compression for static files,
// so we only compress dynamic API responses here to avoid conflicts.
if (!isDev)
{
    builder.Services.AddResponseCompression(opts =>
    {
        opts.EnableForHttps = true;

        // Only compress dynamic content types (API responses, dynamic HTML).
        // Static assets are handled by MapStaticAssets() with pre-compression.
        opts.MimeTypes = new[]
        {
            "application/json",
            "text/plain"
        };
    });
}

var app = builder.Build();

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
    return Results.Text(Util.RenderFruitLoopyErrorHtml(), "text/html; charset=utf-8");
});

// Fallback to index.html for Blazor WASM client-side routing
// This must come AFTER all other specific route mappings
app.MapFallbackToFile("index.html");

app.Run();
