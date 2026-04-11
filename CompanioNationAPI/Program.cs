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
