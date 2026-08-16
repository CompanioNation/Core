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
