using CompanioNation.Shared;

namespace CompanioNationAPI;

/// <summary>
/// Pure decision helper for stale push-token cleanup. Kept as a static,
/// dependency-free type so the ownership rule is unit-testable without a hub.
/// </summary>
internal static class PushCleanup
{
    /// <summary>
    /// Returns the user ID whose push token should be cleared when a push send
    /// fails, or 0 when there is nothing to clear. The returned ID is the token
    /// OWNER (the message recipient) — never the sender.
    /// </summary>
    internal static int GetUserIdToClear(SendMessageResult? parameters)
        => parameters is { PushTokenUserId: > 0 } ? parameters.PushTokenUserId : 0;
}
