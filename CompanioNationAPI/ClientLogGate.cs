namespace CompanioNationAPI;

/// <summary>
/// Server-side gate for client-submitted log reports (hub LogError / LogClientError).
/// A stale or hostile client can invoke those methods in a tight loop, which would
/// otherwise flood the shared admin email budget (6 per 30 min across ALL error
/// sources) and starve genuine production alerts. The gate bounds each SignalR
/// connection to a fixed number of accepted reports per window, drops exact-duplicate
/// content, and keeps its own memory bounded so the defense itself cannot be turned
/// into an OOM vector. Rejected reports are still counted; occasional one-line
/// summaries keep the flood visible without re-flooding the pipeline.
/// </summary>
public static class ClientLogGate
{
    private const int MaxReportsPerWindow = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    // Same content within the dedupe window is dropped regardless of rate.
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(30);

    // Hard cap on accepted payload size — client log bodies are untrusted input.
    internal const int MaxPayloadLength = 8_192;

    // Bounds for the tracking dictionaries. Sized far above legitimate traffic;
    // when exceeded (active abuse), new connections are rejected outright until
    // entries age out, so memory stays O(connections-that-recently-logged).
    private const int MaxTrackedConnections = 10_000;
    private const int MaxHashEntries = 50_000;

    private static readonly object Lock = new();
    private static readonly Dictionary<string, Queue<DateTime>> ConnectionWindows = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DateTime> RecentHashes = new(StringComparer.Ordinal);

    private static int _acceptedTotal;
    private static int _rateLimitedTotal;
    private static int _duplicateTotal;

    /// <summary>Outcome of evaluating one client log submission.</summary>
    public enum Decision
    {
        /// <summary>Log it normally.</summary>
        Accept,
        /// <summary>Over this connection's rate limit — drop silently.</summary>
        RateLimited,
        /// <summary>Identical content already logged recently — drop silently.</summary>
        Duplicate
    }

    /// <summary>
    /// Evaluates a client log submission against the per-connection rate limit and the
    /// duplicate-content window. <paramref name="payloadKey"/> must identify the report
    /// content (e.g. a hash of the message body); connection identity comes from the hub.
    /// </summary>
    public static Decision Evaluate(string connectionId, string payloadKey)
    {
        if (string.IsNullOrEmpty(connectionId))
            return Decision.Accept;

        DateTime now = DateTime.UtcNow;

        lock (Lock)
        {
            Prune(now);

            // Dedupe first: identical content from any connection inside the duplicate
            // window is dropped even if the connection is under its rate limit.
            if (RecentHashes.TryGetValue(payloadKey, out _))
            {
                _duplicateTotal++;
                return Decision.Duplicate;
            }

            if (!ConnectionWindows.TryGetValue(connectionId, out Queue<DateTime>? window))
            {
                if (ConnectionWindows.Count >= MaxTrackedConnections)
                {
                    // Under active flooding with fresh connection IDs: reject rather than
                    // grow unbounded. Entries only leave via pruning, so this recovers.
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

            if (RecentHashes.Count >= MaxHashEntries)
            {
                // Prefer dropping dedupe coverage over exhausting memory during an attack.
                RecentHashes.Clear();
            }
            RecentHashes[payloadKey] = now;

            _acceptedTotal++;
            return Decision.Accept;
        }
    }

    /// <summary>
    /// Stable key for duplicate detection over untrusted text. Truncates before hashing
    /// so oversized payloads cost the same as capped ones and cannot expand memory.
    /// </summary>
    public static string BuildPayloadKey(string? message)
    {
        string normalized = message ?? string.Empty;
        if (normalized.Length > MaxPayloadLength)
            normalized = normalized[..MaxPayloadLength];

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
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
        int duplicates = Interlocked.CompareExchange(ref _duplicateTotal, 0, 0);
        int totalDrops = rateLimited + duplicates;

        // Report at most every ~25 drops; keeps visibility without log spam under load.
        if (totalDrops == 0 || totalDrops % 25 != 0)
            return;

        ErrorLog.LogInfo(
            $"ClientLogGate: {reason} — lifetime totals: {accepted} accepted, " +
            $"{rateLimited} rate-limited, {duplicates} duplicates dropped.");
    }

    /// <summary>Removes window/hash entries that have aged past their retention windows.</summary>
    private static void Prune(DateTime now)
    {
        // Called under Lock.

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

        if (RecentHashes.Count > 0)
        {
            List<string>? expiredHashes = null;
            foreach ((string hash, DateTime seenAt) in RecentHashes)
            {
                if (now - seenAt > DuplicateWindow)
                    (expiredHashes ??= []).Add(hash);
            }

            if (expiredHashes is not null)
            {
                foreach (string hash in expiredHashes)
                    RecentHashes.Remove(hash);
            }
        }
    }
}
