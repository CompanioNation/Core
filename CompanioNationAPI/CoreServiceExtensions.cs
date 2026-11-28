using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CompanioNation.Shared;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CompanioNationAPI;

/// <summary>
/// Registers all shared CompanioNation Core services into the DI container.
/// Call <see cref="AddCompanioNationCore"/> from the host's Program.cs so that
/// both the Core standalone host and the Services production host share the
/// same registrations.  Services-only code (AI providers, email, error logging,
/// billing, etc.) is added separately by the Services host after this call.
/// </summary>
public static class CoreServiceExtensions
{
    /// <summary>
    /// Adds every shared core service: Database, maintenance, SignalR, controllers,
    /// push notifications, response compression, and a default CompanioNita stub.
    /// The CompanioNita registration uses <see cref="ServiceLifetime.Singleton"/>
    /// with <c>TryAdd</c> semantics — register a custom factory <b>before</b>
    /// calling this method to override the default stub.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="isDev">
    /// Pass <c>true</c> in development to skip response compression
    /// (keeps Hot Reload friendly).
    /// </param>
    public static IServiceCollection AddCompanioNationCore(
        this IServiceCollection services, bool isDev)
    {
        // Controllers
        services.AddControllers();

        // SignalR
        var isStaging = string.Equals(
            Environment.GetEnvironmentVariable("SLOT_HOSTNAME"),
            "staging",
            StringComparison.OrdinalIgnoreCase);
        var detailedErrorsRequested = string.Equals(
            Environment.GetEnvironmentVariable("SIGNALR_DETAILED_ERRORS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
            // Allow several in-flight hub invocations per client (e.g. a streaming AI
            // call running alongside chat/settings calls). The default of 1 serializes
            // every hub call from the same connection, which made the messages-pane
            // CompanioNita insight appear to freeze the whole UI while generating.
            options.MaximumParallelInvocationsPerClient = 5;
            // Detailed hub errors expose exception messages to clients. Enable them
            // automatically in dev/staging, and allow an explicit opt-in on production
            // via the SIGNALR_DETAILED_ERRORS app setting for incident debugging.
            options.EnableDetailedErrors = isDev || isStaging || detailedErrorsRequested;

            // Hub filters ONLY take effect when added here. Registering an IHubFilter in DI
            // does not put it in the invocation pipeline (SignalR builds the chain from these
            // AddFilter calls alone), and getting that wrong silently disabled BOTH filters:
            // the cutover gate let every hub call through while a promotion had the server
            // frozen, and one user message was written into the database being replaced.
            // Order matters: the gate is outermost, so a refused call never runs anything.
            options.AddFilter<CutoverGateHubFilter>();
            options.AddFilter<ErrorLoggingHubFilter>();
        });
        // The concrete types must be resolvable for AddFilter above; the interface
        // registrations below keep both filters injectable wherever an IHubFilter is asked for.
        services.AddSingleton<ErrorLoggingHubFilter>();
        services.AddSingleton<CutoverGateHubFilter>();
        services.AddSingleton<IHubFilter>(sp => sp.GetRequiredService<ErrorLoggingHubFilter>());
        services.AddSingleton<IHubFilter>(sp => sp.GetRequiredService<CutoverGateHubFilter>());

        // Database
        services.AddSingleton<Database>();

        // CompanioNita default stub — Services host can pre-register a factory
        // override before calling this method; TryAddSingleton won't replace it.
        services.TryAddSingleton<CompanioNita>();

        // Background maintenance
        services.AddSingleton<MaintenanceEventService>();
        services.AddHostedService<MaintenanceEventService>();

        // Push notifications — VAPID for web, FCM for native iOS/Android
        services.AddSingleton<VapidPushService>();
        services.AddSingleton<FcmPushService>();
        services.AddSingleton<IPushService, CompositePushService>();

        // Response compression (dynamic content only — MapStaticAssets handles static)
        if (!isDev)
        {
            services.AddResponseCompression(opts =>
            {
                opts.EnableForHttps = true;
                opts.MimeTypes = new[]
                {
                    "application/json",
                    "text/plain"
                };
            });
        }

        return services;
    }

    /// <summary>
    /// Loads environment variables from a key=value file when it exists.
    /// Lines starting with # and blank lines are ignored.
    /// </summary>
    public static void LoadEnvFileIfPresent(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;

            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            Environment.SetEnvironmentVariable(key, val);
        }
    }

    /// <summary>
    /// Maps the email unsubscribe endpoint. Gmail (and other one-click clients) POSTs
    /// here from the <c>List-Unsubscribe-Post</c> header; a human clicking the footer
    /// link lands on the Blazor <c>/Unsubscribe</c> page instead. Both share the same
    /// token format, so the endpoint can also serve as the one-click fallback.
    /// </summary>
    public static IEndpointConventionBuilder MapEmailUnsubscribe(this WebApplication app)
    {
        return app.MapPost("/api/unsubscribe/{token}", async (string token, Database db) =>
        {
            string secret = Environment.GetEnvironmentVariable(SecureUrlPayload.SecretEnvironmentVariable) ?? string.Empty;
            if (!EmailUnsubscribe.TryOpenToken(token, secret, out string? email))
                // 410 Gone: the link is real but expired/consumed — an expected outcome,
                // not a server error. Mail clients treat 2xx/410 as a successful opt-out.
                return Results.Problem("This unsubscribe link is invalid or has expired.", statusCode: StatusCodes.Status410Gone);

            bool ok = await db.SetEmailEnabledAsync(email!, false);
            if (!ok)
                return Results.Problem("Could not update email preferences.", statusCode: StatusCodes.Status500InternalServerError);

            return Results.Ok(new { email });
        });
    }

    private const string EmailEventsTokenEnvironmentVariable = "COMPANIONATION_EMAIL_EVENTS_TOKEN";

    /// <summary>
    /// Maps the Azure Communication Services email event webhook (Event Grid). Delivery
    /// reports are what turn a submitted email into a known outcome: a bounce marks the
    /// recipient address undeliverable so the app can offer a recovery path, and a spam hold
    /// is recorded too so the app can ask the owner to check their spam folder and add us to
    /// their contacts.
    /// </summary>
    /// <remarks>
    /// Anonymous by necessity (Event Grid cannot hold a session), so it is gated on a shared
    /// secret supplied in the webhook URL (<c>?token=</c>) or the
    /// <c>X-Email-Events-Token</c> header, compared in constant time.
    /// </remarks>
    public static IEndpointConventionBuilder MapEmailEvents(this WebApplication app)
    {
        return app.MapPost("/api/email/events", async (HttpContext context, Database db) =>
        {
            string expected = Environment.GetEnvironmentVariable(EmailEventsTokenEnvironmentVariable) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expected))
            {
                // A REAL misconfiguration: the endpoint is live but nobody can authenticate,
                // so every bounce report would be lost.
                await ErrorLog.LogErrorMessage("Email events endpoint called but COMPANIONATION_EMAIL_EVENTS_TOKEN is not set; delivery reports cannot be accepted.");
                return Results.Problem("Email event handling is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            string supplied = context.Request.Query["token"].ToString();
            if (supplied.Length == 0)
                supplied = context.Request.Headers["X-Email-Events-Token"].ToString();

            if (!TokenEquals(expected, supplied))
            {
                await ErrorLog.LogErrorMessage("Email events endpoint rejected a request with an invalid token.");
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(context.Request.Body);
            }
            catch (JsonException ex)
            {
                await ErrorLog.LogErrorException(ex, "Email events endpoint received malformed JSON");
                return Results.BadRequest();
            }

            using (document)
            {
                string? validationCode = EmailDeliveryEventParser.ReadValidationCode(document);
                if (validationCode is not null)
                {
                    // Event Grid handshake: echo the code so the subscription becomes active.
                    return Results.Ok(new { validationResponse = validationCode });
                }

                bool writeFailed = false;

                foreach (EmailDeliveryEvent deliveryEvent in EmailDeliveryEventParser.ParseDeliveryReports(document))
                {
                    // One bad event must never abort the batch, because Event Grid would retry
                    // the whole request and replay the events that already succeeded (the write
                    // is idempotent, so a replay is harmless). A failure is remembered and turned
                    // into a retryable response instead: answering 200 would silently lose the
                    // report, and Event Grid never retries a 2xx.
                    if (!await db.RecordEmailDeliveryStatusAsync(
                        deliveryEvent.Recipient,
                        deliveryEvent.State,
                        deliveryEvent.State == EmailDeliveryState.Delivered ? null : deliveryEvent.Error))
                    {
                        writeFailed = true;
                    }
                }

                if (writeFailed)
                {
                    return Results.Problem("Recording email delivery status failed; the event will be retried.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }

            return Results.Ok();
        });
    }

    private static bool TokenEquals(string expected, string supplied)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));

    /// <summary>
    /// Maps the same-origin photo proxy endpoint. Local development runs the PWA on
    /// HTTPS (https://localhost:7114) while Azurite serves blobs over plain HTTP, and
    /// browsers block that mixed content, so images fail with "Resource load error".
    /// This endpoint streams the blob bytes over the app's own HTTPS origin instead.
    /// Production keeps COMPANIONATION_PHOTO_BASE_URL pointing at the public blob CDN
    /// URL and never routes image requests through here.
    /// </summary>
    public static IEndpointConventionBuilder MapCompanioNationPhotoProxy(this WebApplication app)
    {
        return app.MapGet("/api/photos/{fileName}", async (string fileName, Database db, HttpResponse response) =>
        {
            if (!Util.TryGetImageGuidFromBlobName(fileName, out var imageGuid))
                return Results.NotFound();

            // Blobs written by the app live at the container root ({guid}.jpg), but Azurite
            // workspaces seeded from production keep them under a "photos" virtual folder.
            // Try both so the dev emulator displays photos regardless of layout.
            byte[]? imageBytes = await db.DownloadBlobFromAzureAsync(imageGuid)
                ?? await db.DownloadBlobFromAzureAsync(imageGuid, virtualDirectory: "photos");

            if (imageBytes == null)
                return Results.NotFound();

            // A photo GUID is immutable, so its bytes never change — cache forever.
            response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(imageBytes, "image/jpeg", enableRangeProcessing: true);
        });
    }
}
