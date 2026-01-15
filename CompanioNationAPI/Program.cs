using CompanioNationAPI;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Diagnostics;

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

static string RenderFruitLoopyErrorHtml()
{
    return """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>CompanioNation - Error</title>
  <style>
    body{font-family:Arial,Helvetica,sans-serif;max-width:900px;margin:0 auto;padding:24px;line-height:1.6;}
    header{display:flex;align-items:center;gap:12px;margin-bottom:16px;}
    header img{height:256px;width:256px;}
    .card{border:1px solid #e0e0e0;border-radius:8px;padding:16px;box-shadow:0 2px 4px rgba(0,0,0,0.05);}a{color:#1565c0;text-decoration:none;font-weight:700;}a:hover{text-decoration:underline;}
    footer{margin-top:24px;font-size:0.9em;color:#666;}
  </style>
</head>
<body>
  <header>
    <img src="/images/CompanioNita.png" alt="CompanioNita" />
    <div>
      <h1>Well… that went fruit loopy 🍍</h1>
      <p style="margin-top:4px;color:#555;">CompanioNita tripped over a server-side banana peel.</p>
    </div>
  </header>

  <div class="card">
    <p>Try again, or head back home.</p>
    <p><a href="/">Return to CompanioNation</a></p>
  </div>

  <footer>This error has been logged.</footer>
</body>
</html>
""";
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // TODO - maybe use HSTS in future?
    //app.UseHsts();
}

app.UseHttpsRedirection();

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
    return Results.Text(RenderFruitLoopyErrorHtml(), "text/html; charset=utf-8");
});

// Fallback to index.html for Blazor WASM client-side routing
// This must come AFTER all other specific route mappings
app.MapFallbackToFile("index.html");

app.Run();
