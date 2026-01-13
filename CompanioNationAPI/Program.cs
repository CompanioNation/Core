using CompanioNationAPI;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Azure;

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
            .WithOrigins("https://localhost:7075")
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

// Response compression: only in non-dev (Hot Reload friendliness)
if (!isDev)
{
    builder.Services.AddResponseCompression(opts =>
    {
        opts.EnableForHttps = true;

        // Don't add application/octet-stream by default (often images/binary, usually already compressed).
        opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
        {
            "application/json",
            "text/plain",
            "text/html",
            "text/css",
            "application/javascript",
            "image/svg+xml"
        });
    });
}


var app = builder.Build();

if (!isDev)
    app.UseResponseCompression();

app.UseHttpsRedirection();

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

app.Run();
