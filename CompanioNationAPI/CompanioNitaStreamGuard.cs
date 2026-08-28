using System.Collections.Concurrent;

namespace CompanioNationAPI;

/// <summary>
/// In-process guard that prevents more than one CompanioNita insight stream from
/// running at a time for the same user/conversation. Held by the hub for the full
/// request lifetime — including persistence and push notification — so a rapid
/// repeat request cannot slip in after streaming but before the advice message is
/// committed to the database.
/// </summary>
internal static class CompanioNitaStreamGuard
{
    private static readonly ConcurrentDictionary<string, byte> s_inFlight = new(StringComparer.Ordinal);

    /// <summary>Returns true when the key was acquired, false when a stream is already in flight for it.</summary>
    public static bool TryStart(string key)
    {
        return s_inFlight.TryAdd(key, 0);
    }

    /// <summary>Releases the in-flight slot for the key.</summary>
    public static void Finish(string key)
    {
        s_inFlight.TryRemove(key, out _);
    }

    /// <summary>Clears all in-flight slots. Test support only.</summary>
    internal static void Reset()
    {
        s_inFlight.Clear();
    }
}
