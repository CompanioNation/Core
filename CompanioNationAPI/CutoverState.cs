namespace CompanioNationAPI;

/// <summary>
/// In-memory, per-instance cutover flag. During a staging→production promotion the
/// script activates this on the live slot so the app returns a branded 503 (instead
/// of an Azure "403 web app stopped" page) and drops pooled DB connections. The App
/// Service itself is never stopped or started.
/// </summary>
/// <remarks>
/// Lives in the API assembly (not the host) so the SignalR hub can also observe the
/// flag: while the gate is active the hub must refuse writes, otherwise a client that
/// is still connected during the freeze would write into the old production database
/// that the promotion is about to replace. Set is called by the host's set-active
/// endpoint; the hub and the host share one process, so they share this static.
/// </remarks>
public static class CutoverState
{
    public const string DefaultMessage =
        "CompanioNation is undergoing scheduled maintenance. Please try again in a few minutes.";

    private static volatile bool _active;
    private static string _message = DefaultMessage;

    /// <summary>True while the cutover gate is active (all non-allowlisted requests return 503).</summary>
    public static bool Active => _active;

    /// <summary>The operator-provided message shown on the branded 503 page.</summary>
    public static string Message => _message;

    /// <summary>Sets the in-memory cutover state for this process instance.</summary>
    public static void Set(bool active, string? message = null)
    {
        _message = string.IsNullOrWhiteSpace(message) ? DefaultMessage : message;
        _active = active;
    }
}

/// <summary>Body contract for POST /api/cutover/set-active.</summary>
public sealed record CutoverSetActiveRequest(bool Active, string? Message = null);
