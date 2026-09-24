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

    // ──── Progressive login lockout (per-IP escalation) ────
    // The sliding window above is the first line (N attempts per minute). Sustained
    // abuse escalates: every attempt while blocked (or every failed password beyond the
    // allowance) is a STRIKE that DOUBLES the block — "the harder they try, the longer
    // they wait" — capped so a shared mobile-carrier NAT IP cannot be locked out for a
    // day. A successful login clears the strikes; 30 quiet minutes decay them.
    internal static readonly TimeSpan StrikeBackoffBase = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan StrikeBackoffCap = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan StrikeDecayHorizon = TimeSpan.FromMinutes(30);

    /// <summary>Strike count at which the block has grown to >= 16 minutes — a real attack signal worth alerting on, once.</summary>
    internal const int DeepLockoutStrikeCount = 10;

    private sealed class LoginStrikeState
    {
        public int Strikes;
        public long BlockedUntilUtcTicks;
        public long LastActivityUtcTicks;
    }

    private static readonly ConcurrentDictionary<string, LoginStrikeState> s_loginStrikes = new(StringComparer.Ordinal);

    public static bool IsLoginRateLimited(string ip) => IsLoginRateLimited(ip, DateTime.UtcNow);

    /// <summary>Time-injectable seam so escalation is unit-testable without sleeping.</summary>
    internal static bool IsLoginRateLimited(string ip, DateTime utcNow)
    {
        var state = s_loginStrikes.GetOrAdd(ip, _ => new LoginStrikeState());
        lock (state)
        {
            // Decay stale strikes only after a genuinely quiet horizon — an active attacker
            // must never get a reset, but yesterday's typos must not haunt a user today.
            if (state.BlockedUntilUtcTicks < utcNow.Ticks &&
                utcNow.Ticks - state.LastActivityUtcTicks > StrikeDecayHorizon.Ticks)
            {
                state.Strikes = 0;
            }

            // Attempting WHILE blocked is itself a strike: it extends the lockout.
            if (state.BlockedUntilUtcTicks > utcNow.Ticks)
            {
                RegisterStrike(state, utcNow);
                return true;
            }

            if (IsRateLimited(s_loginAttempts, ip, MaxLoginAttemptsPerWindow, LoginRateWindow))
            {
                RegisterStrike(state, utcNow);
                return true;
            }

            state.LastActivityUtcTicks = utcNow.Ticks;
            return false;
        }
    }

    /// <summary>
    /// Records a FAILED password login. Beyond the attempt allowance every failure
    /// starts/extends a block, so sustained password guessing slows itself down.
    /// </summary>
    public static void NoteLoginFailure(string ip) => NoteLoginFailure(ip, DateTime.UtcNow);

    internal static void NoteLoginFailure(string ip, DateTime utcNow)
    {
        var state = s_loginStrikes.GetOrAdd(ip, _ => new LoginStrikeState());
        lock (state)
        {
            RegisterStrike(state, utcNow);
        }
    }

    /// <summary>Clears the escalation state after a successful login so a user who simply mistyped is not punished afterwards.</summary>
    public static void NoteLoginSuccess(string ip) => NoteLoginSuccess(ip, DateTime.UtcNow);

    internal static void NoteLoginSuccess(string ip, DateTime utcNow)
    {
        if (s_loginStrikes.TryRemove(ip, out var state))
        {
            lock (state)
            {
                state.Strikes = 0;
                state.BlockedUntilUtcTicks = 0;
            }
        }

        // A successful login also clears the sliding attempt window. Otherwise the
        // just-authorized user would still be sitting above the 5-attempts/minute line
        // and be falsely rate-limited if they need to sign in again within that minute.
        s_loginAttempts.TryRemove(ip, out _);
    }

    private static void RegisterStrike(LoginStrikeState state, DateTime utcNow)
    {
        state.Strikes++;
        state.LastActivityUtcTicks = utcNow.Ticks;

        var backoff = BackoffFor(state.Strikes);
        if (backoff > TimeSpan.Zero)
        {
            var until = (utcNow + backoff).Ticks;
            if (until > state.BlockedUntilUtcTicks)
                state.BlockedUntilUtcTicks = until;
        }

        // A block that has grown past 16 minutes means sustained abuse, not a user
        // mistyping a password. Alert EXACTLY ONCE per crossing — the error-email budget
        // bounds the rest — so a real attack is loud but a typo is never an email.
        if (state.Strikes == DeepLockoutStrikeCount)
        {
            _ = ErrorLog.LogErrorMessage(
                $"SECURITY: possible brute-force login attack — IP blocked with escalating lockout " +
                $"(strike {state.Strikes}, backoff {backoff.TotalMinutes:0} min). " +
                "Failed-login targets are in the info log: search 'Security: failed password login'.");
        }
    }

    /// <summary>
    /// The escalating timeout: no block within the attempt allowance, then 1 minute
    /// doubling per further strike, capped at <see cref="StrikeBackoffCap"/>.
    /// </summary>
    internal static TimeSpan BackoffFor(int strikes)
    {
        int over = strikes - MaxLoginAttemptsPerWindow;
        if (over <= 0)
            return TimeSpan.Zero;

        // Clamp the exponent before shifting so a pathological strike count cannot overflow.
        int exponent = Math.Min(over - 1, 10);
        var backoff = TimeSpan.FromTicks(StrikeBackoffBase.Ticks * (1L << exponent));
        return backoff > StrikeBackoffCap ? StrikeBackoffCap : backoff;
    }

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

        // Strike/block state ages out on the same schedule as the attempt queues so a
        // long-lived process does not accumulate one entry per unique IP.
        foreach (var kvp in s_loginStrikes)
        {
            if (DateTime.UtcNow.Ticks - kvp.Value.LastActivityUtcTicks > StrikeDecayHorizon.Ticks &&
                kvp.Value.BlockedUntilUtcTicks < DateTime.UtcNow.Ticks)
            {
                s_loginStrikes.TryRemove(new KeyValuePair<string, LoginStrikeState>(kvp.Key, kvp.Value));
            }
        }
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
