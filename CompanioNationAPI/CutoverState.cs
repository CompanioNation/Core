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
    private static int _inFlight;

    /// <summary>True while the cutover gate is active (all non-allowlisted requests return 503).</summary>
    public static bool Active => _active;

    /// <summary>The operator-provided message shown on the branded 503 page.</summary>
    public static string Message => _message;

    /// <summary>
    /// Hub invocations that started before the gate and have not finished. The promotion
    /// script waits for this to reach zero before it copies data, because a call already
    /// running when the gate closes (a CompanioNita stream can run for many seconds) would
    /// otherwise write into the database after its table was exported.
    /// </summary>
    public static int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>Sets the in-memory cutover state for this process instance.</summary>
    public static void Set(bool active, string? message = null)
    {
        _message = string.IsNullOrWhiteSpace(message) ? DefaultMessage : message;
        _active = active;
        // Full fence: the write above must be visible before any later InFlight read, so a
        // caller that already passed the gate check is guaranteed to be counted.
        Interlocked.MemoryBarrier();
    }

    /// <summary>
    /// Registers a unit of work, or returns null when the gate is active (the caller must
    /// refuse). The count is incremented BEFORE the gate is read, so a call is either
    /// refused or counted — never neither. Dispose the token when the work finishes.
    /// </summary>
    public static IDisposable? TryBeginWork()
    {
        Interlocked.Increment(ref _inFlight);
        if (!_active)
            return new WorkToken();

        Interlocked.Decrement(ref _inFlight);
        return null;
    }

    private sealed class WorkToken : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref _inFlight);
        }
    }
}

/// <summary>Body contract for POST /api/cutover/set-active.</summary>
public sealed record CutoverSetActiveRequest(bool Active, string? Message = null);
