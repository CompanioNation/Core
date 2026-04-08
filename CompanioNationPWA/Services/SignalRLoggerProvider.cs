using Microsoft.Extensions.Logging;

namespace CompanioNationPWA.Services;

/// <summary>
/// Forwards error/critical-level log messages to the server via SignalR.
/// This catches Blazor framework-level unhandled exceptions (the ones that
/// show #blazor-error-ui) which bypass ErrorBoundary.OnErrorAsync entirely.
/// </summary>
internal sealed class SignalRLoggerProvider(IServiceProvider serviceProvider) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new SignalRLogger(serviceProvider, categoryName);

    public void Dispose() { }
}

internal sealed class SignalRLogger(IServiceProvider serviceProvider, string categoryName) : ILogger
{
    // Only forward Error and Critical — anything less is noise
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);

        // Avoid logging our own logging failures (prevent infinite recursion)
        if (categoryName.Contains("SignalRLogger", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var client = serviceProvider.GetService<CompanioNationSignalRClient>();
            if (client is null)
            {
                return;
            }

            var fullMessage = $"[{logLevel}] [{categoryName}] {message}";

            // Fire-and-forget — we're inside a sync ILogger.Log call so we can't await.
            // LogError already has its own try/catch with LogErrorPassive fallback.
            _ = client.LogError(fullMessage);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SignalRLogger failed to forward error: {ex.Message}");
        }
    }
}
