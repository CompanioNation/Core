using System.Collections.Concurrent;

namespace CompanioNationAPI;

/// <summary>
/// Sliding-window IP rate limiter shared by the SignalR hub and the REST auth
/// endpoints, so both paths apply the exact same limits. Dictionaries are pruned
/// of empty per-IP entries so a long-lived process does not accumulate memory
/// per unique IP address.
/// </summary>
public static class LoginRateLimiter
{
    public const int MaxLoginAttemptsPerWindow = 5;
    public static readonly TimeSpan LoginRateWindow = TimeSpan.FromSeconds(60);

    public const int MaxUnauthAttemptsPerWindow = 10;
    public static readonly TimeSpan UnauthRateWindow = TimeSpan.FromMinutes(1);

    public const int MaxSignupAttemptsPerWindow = 3;
    public static readonly TimeSpan SignupRateWindow = TimeSpan.FromMinutes(10);

    private static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> s_loginAttempts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> s_unauthAttempts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> s_signupAttempts = new(StringComparer.Ordinal);

    public static bool IsLoginRateLimited(string ip)
        => IsRateLimited(s_loginAttempts, ip, MaxLoginAttemptsPerWindow, LoginRateWindow);

    public static bool IsUnauthRateLimited(string ip)
        => IsRateLimited(s_unauthAttempts, ip, MaxUnauthAttemptsPerWindow, UnauthRateWindow);

    public static bool IsSignupRateLimited(string ip)
        => IsRateLimited(s_signupAttempts, ip, MaxSignupAttemptsPerWindow, SignupRateWindow);

    /// <summary>
    /// Returns true when <paramref name="ip"/> has exceeded <paramref name="maxCount"/>
    /// within the sliding <paramref name="window"/>. Enqueues the current timestamp
    /// before checking the count so the check is conservative under concurrency — a
    /// burst of simultaneous calls all count toward the limit rather than slipping
    /// through a check-then-enqueue gap.
    /// </summary>
    internal static bool IsRateLimited(
        ConcurrentDictionary<string, ConcurrentQueue<DateTime>> store,
        string ip, int maxCount, TimeSpan window)
    {
        SweepEmptyEntries();

        var attempts = store.GetOrAdd(ip, _ => new ConcurrentQueue<DateTime>());

        // Prune entries outside the rate window
        var cutoff = DateTime.UtcNow - window;
        while (attempts.TryPeek(out var ts) && ts < cutoff)
            attempts.TryDequeue(out _);

        // Enqueue first, then inspect — avoids the classic TOCTOU race where
        // two callers both see count == maxCount-1 and both pass through.
        attempts.Enqueue(DateTime.UtcNow);

        return attempts.Count > maxCount;
    }

    private const long SweepIntervalTicks = TimeSpan.TicksPerMinute * 5;
    private static long s_lastSweepUtcTicks;

    /// <summary>
    /// Periodically removes dictionary entries whose queues have become empty
    /// (all timestamps aged out), so a long-lived process does not accumulate a
    /// dictionary entry per unique IP. Bounded work: guarded so only one thread
    /// sweeps per interval, and removal uses the KeyValuePair TryRemove overload
    /// so only the exact queue instance observed can be removed — a queue a
    /// concurrent caller is still using is never lost.
    /// </summary>
    private static void SweepEmptyEntries()
    {
        long now = DateTime.UtcNow.Ticks;
        long last = Interlocked.Read(ref s_lastSweepUtcTicks);
        if (now - last < SweepIntervalTicks)
            return;
        if (Interlocked.CompareExchange(ref s_lastSweepUtcTicks, now, last) != last)
            return;

        Sweep(s_loginAttempts);
        Sweep(s_unauthAttempts);
        Sweep(s_signupAttempts);
    }

    internal static void Sweep(ConcurrentDictionary<string, ConcurrentQueue<DateTime>> store)
    {
        foreach (var kvp in store)
        {
            if (kvp.Value.IsEmpty)
                store.TryRemove(new KeyValuePair<string, ConcurrentQueue<DateTime>>(kvp.Key, kvp.Value));
        }
    }
}
