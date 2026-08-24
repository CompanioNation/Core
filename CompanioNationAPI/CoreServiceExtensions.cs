using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        });
        services.AddSingleton<IHubFilter, ErrorLoggingHubFilter>();

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
}
