namespace CompanioNationAPI;

/// <summary>
/// Server-side gate for client-submitted log reports (hub LogError / LogClientError).
/// A stale or hostile client can invoke those methods in a tight loop, which would
/// otherwise flood the shared admin email budget (6 per 30 min across ALL error
/// sources) and starve genuine production alerts. The gate bounds each SignalR
/// connection to a fixed number of accepted reports per window and keeps its own memory
/// bounded so the defense itself cannot be turned into an OOM vector. Rejected reports
/// are still counted; occasional one-line summaries keep the flood visible without
/// re-flooding the pipeline.
/// </summary>
public static class ClientLogGate
{
    private const int MaxReportsPerWindow = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    // Hard cap on accepted payload size — client log bodies are untrusted input.
    // Sized to fit a batched backlog: the client sends its entire stored error buffer
    // (up to 25 entries) in a single LogError call rather than one call per entry, so a
    // lower ceiling would truncate the tail of a large batch.
    internal const int MaxPayloadLength = 65_536;

    // Bounds for the tracking dictionary. Sized far above legitimate traffic; when
    // exceeded (active abuse), new connections are rejected outright until entries
    // age out, so memory stays O(connections-that-recently-logged).
    private const int MaxTrackedConnections = 10_000;

    private static readonly object Lock = new();
    private static readonly Dictionary<string, Queue<DateTime>> ConnectionWindows = new(StringComparer.Ordinal);

    private static int _acceptedTotal;
    private static int _rateLimitedTotal;

    /// <summary>Outcome of evaluating one client log submission.</summary>
    public enum Decision
    {
        /// <summary>Log it normally.</summary>
        Accept,
        /// <summary>Over this connection's rate limit — drop silently.</summary>
        RateLimited
    }

    /// <summary>
    /// Evaluates a client log submission against the per-connection rate limit. Connection
    /// identity comes from the hub.
    /// </summary>
    public static Decision Evaluate(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return Decision.Accept;

        DateTime now = DateTime.UtcNow;

        lock (Lock)
        {
            // O(1) per request: expire only THIS connection's stale timestamps, never a
            // whole-table scan. Empty windows are dropped so they can't accumulate.
            if (ConnectionWindows.TryGetValue(connectionId, out Queue<DateTime>? window))
            {
                while (window.Count > 0 && now - window.Peek() > Window)
                    window.Dequeue();

                if (window.Count == 0)
                    ConnectionWindows.Remove(connectionId);
            }

            if (!ConnectionWindows.TryGetValue(connectionId, out window))
            {
                if (ConnectionWindows.Count >= MaxTrackedConnections)
                {
                    // At capacity, do a SINGLE amortized sweep instead of rejecting outright.
                    // A storm of one-shot connection IDs can fill the table; one O(n) cleanup
                    // frees the aged-out entries so legitimate connections aren't blocked.
                    ReapStale(now);
                }

                if (ConnectionWindows.Count >= MaxTrackedConnections)
                {
                    // Still full after the reap — genuine active flood, reject.
                    _rateLimitedTotal++;
                    return Decision.RateLimited;
                }

                window = new Queue<DateTime>();
                ConnectionWindows[connectionId] = window;
            }

            if (window.Count >= MaxReportsPerWindow)
            {
                _rateLimitedTotal++;
                return Decision.RateLimited;
            }

            window.Enqueue(now);

            _acceptedTotal++;
            return Decision.Accept;
        }
    }

    /// <summary>
    /// Logs a one-line summary of dropped submissions when enough have accumulated to be
    /// worth surfacing. Called by the ingestion points after a rejection so the flood is
    /// visible in server logs without emailing anyone.
    /// </summary>
    public static void LogDropSummaryIfWarranted(string reason)
    {
        int accepted = Interlocked.CompareExchange(ref _acceptedTotal, 0, 0);
        int rateLimited = Interlocked.CompareExchange(ref _rateLimitedTotal, 0, 0);

        // Report at most every ~25 drops; keeps visibility without log spam under load.
        if (rateLimited == 0 || rateLimited % 25 != 0)
            return;

        ErrorLog.LogInfo(
            $"ClientLogGate: {reason} — lifetime totals: {accepted} accepted, " +
            $"{rateLimited} rate-limited.");
    }

    /// <summary>
    /// One O(n) cleanup of aged-out windows, run ONLY when the table hits its capacity —
    /// amortized so the per-request path stays O(1). Called under <see cref="Lock"/>.
    /// </summary>
    private static void ReapStale(DateTime now)
    {
        List<string>? expiredConnections = null;
        foreach ((string connectionId, Queue<DateTime> window) in ConnectionWindows)
        {
            while (window.Count > 0 && now - window.Peek() > Window)
                window.Dequeue();

            if (window.Count == 0)
                (expiredConnections ??= []).Add(connectionId);
        }

        if (expiredConnections is not null)
        {
            foreach (string connectionId in expiredConnections)
                ConnectionWindows.Remove(connectionId);
        }
    }
}
