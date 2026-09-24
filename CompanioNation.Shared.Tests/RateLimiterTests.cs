using System.Collections.Concurrent;
using CompanioNationAPI;

namespace CompanioNation.Shared.Tests;

public class RateLimiterTests
{
    private static (ConcurrentDictionary<string, ConcurrentQueue<DateTime>> Store, string Ip) NewStore()
        => (new ConcurrentDictionary<string, ConcurrentQueue<DateTime>>(StringComparer.Ordinal), "1.2.3.4");

    [Fact]
    public void WhenExactlyAtLimitThenNotRateLimited()
    {
        var (store, ip) = NewStore();

        bool limited = false;
        for (int i = 0; i < 5; i++)
            limited = LoginRateLimiter.IsRateLimited(store, ip, 5, TimeSpan.FromMinutes(1));

        Assert.False(limited);
    }

    [Fact]
    public void WhenOverLimitThenRateLimited()
    {
        var (store, ip) = NewStore();

        bool limited = false;
        for (int i = 0; i < 6; i++)
            limited = LoginRateLimiter.IsRateLimited(store, ip, 5, TimeSpan.FromMinutes(1));

        Assert.True(limited);
    }

    [Fact]
    public void WhenWindowSlidesThenOldAttemptsExpire()
    {
        var (store, ip) = NewStore();

        // An attempt from outside the window must be pruned before the new one
        // counts, so a single old entry cannot trip the limit.
        store.GetOrAdd(ip, _ => new ConcurrentQueue<DateTime>())
            .Enqueue(DateTime.UtcNow.AddMinutes(-2));

        bool limited = LoginRateLimiter.IsRateLimited(store, ip, 1, TimeSpan.FromMinutes(1));

        Assert.False(limited);
    }

    [Fact]
    public void WhenSweepRunsThenEmptyEntriesAreRemoved()
    {
        var store = new ConcurrentDictionary<string, ConcurrentQueue<DateTime>>(StringComparer.Ordinal);
        store.TryAdd("stale-ip", new ConcurrentQueue<DateTime>());
        store.GetOrAdd("active-ip", _ => new ConcurrentQueue<DateTime>()).Enqueue(DateTime.UtcNow);

        LoginRateLimiter.Sweep(store);

        Assert.False(store.ContainsKey("stale-ip"));
        Assert.True(store.ContainsKey("active-ip"));
    }
}

/// <summary>
/// Progressive login lockout: sustained abuse must escalate ("the harder they try,
/// the longer they wait") while a user who simply mistyped their password must never
/// stay punished. Each test uses a unique IP so the shared limiter state cannot
/// leak between tests.
/// </summary>
public class ProgressiveLockoutTests
{
    private static string NewIp() => $"test-{Guid.NewGuid():N}";

    [Fact]
    public void WhenWithinAttemptAllowanceThenNoBackoff()
    {
        Assert.Equal(TimeSpan.Zero, LoginRateLimiter.BackoffFor(1));
        Assert.Equal(TimeSpan.Zero, LoginRateLimiter.BackoffFor(5));
    }

    [Fact]
    public void WhenStrikesGrowThenBackoffDoubles()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), LoginRateLimiter.BackoffFor(6));
        Assert.Equal(TimeSpan.FromMinutes(2), LoginRateLimiter.BackoffFor(7));
        Assert.Equal(TimeSpan.FromMinutes(4), LoginRateLimiter.BackoffFor(8));
    }

    [Fact]
    public void WhenStrikesExplodeThenBackoffIsCapped()
    {
        Assert.Equal(LoginRateLimiter.StrikeBackoffCap, LoginRateLimiter.BackoffFor(50));
    }

    [Fact]
    public void WhenAttemptingWhileBlockedThenLockoutExtends()
    {
        var ip = NewIp();
        var t0 = DateTime.UtcNow;

        for (int i = 0; i < 6; i++)
            LoginRateLimiter.NoteLoginFailure(ip, t0);

        // Blocked for 1 minute from t0; this rejected attempt is a strike too...
        Assert.True(LoginRateLimiter.IsLoginRateLimited(ip, t0.AddSeconds(30)));

        // ...so by t0+2min the block has grown past its original expiry.
        Assert.True(LoginRateLimiter.IsLoginRateLimited(ip, t0.AddMinutes(2)));
    }

    [Fact]
    public void WhenLoginSucceedsThenStrikesAreCleared()
    {
        var ip = NewIp();
        var t0 = DateTime.UtcNow;

        for (int i = 0; i < 6; i++)
            LoginRateLimiter.NoteLoginFailure(ip, t0);

        Assert.True(LoginRateLimiter.IsLoginRateLimited(ip, t0.AddSeconds(30)));

        LoginRateLimiter.NoteLoginSuccess(ip, t0.AddSeconds(30));

        Assert.False(LoginRateLimiter.IsLoginRateLimited(ip, t0.AddSeconds(31)));
    }

    [Fact]
    public void WhenQuietForHorizonThenStrikesDecay()
    {
        var ip = NewIp();
        var t0 = DateTime.UtcNow;

        for (int i = 0; i < 8; i++)
            LoginRateLimiter.NoteLoginFailure(ip, t0);

        // Past the decay horizon with no activity: the next check resets the strikes...
        var later = t0.Add(LoginRateLimiter.StrikeDecayHorizon).AddMinutes(1);
        Assert.False(LoginRateLimiter.IsLoginRateLimited(ip, later));

        // ...so one more failure starts from a clean slate instead of the old 4-minute block.
        LoginRateLimiter.NoteLoginFailure(ip, later);
        Assert.False(LoginRateLimiter.IsLoginRateLimited(ip, later.AddSeconds(10)));
    }
}
