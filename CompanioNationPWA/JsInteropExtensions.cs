using Microsoft.JSInterop;

namespace CompanioNationPWA;

/// <summary>
/// Extension methods for resilient JS interop in Blazor WASM.
/// </summary>
internal static class JsInteropExtensions
{
    private static readonly HashSet<string> _reported = [];

    /// <summary>
    /// Imports a collocated JS module, returning <c>null</c> on transient fetch failures
    /// (service worker cache update, connectivity blip, deployment slot swap).
    /// Callers should null-check before invoking methods on the returned reference.
    /// </summary>
    /// <param name="logError">
    /// Optional callback to report the failure through the standard logging pipeline.
    /// Each module path is reported at most once per app session to avoid email storms.
    /// Pass <c>SignalRClient.LogError</c> to enable server-side notification.
    /// </param>
    internal static async Task<IJSObjectReference?> ImportModuleAsync(
        this IJSRuntime jsRuntime, string modulePath,
        Func<Exception, string?, Task>? logError = null)
    {
        try
        {
            return await jsRuntime.InvokeAsync<IJSObjectReference>("import", modulePath);
        }
        catch (JSException ex)
        {
            // Always log to browser console for local diagnostics.
            Console.Error.WriteLine(
                $"Failed to load JS module '{modulePath}' — transient fetch error. " +
                "Component will degrade gracefully.");

            // Report through the standard pipeline once per module per session
            // to surface patterns without causing email storms.
            if (logError is not null && _reported.Add(modulePath))
            {
                try
                {
                    await logError(ex, $"JS module import failed: {modulePath}");
                }
                catch
                {
                    // SignalR may not be connected yet; the console log above
                    // already captured the diagnostics.
                }
            }

            return null;
        }
    }
}
