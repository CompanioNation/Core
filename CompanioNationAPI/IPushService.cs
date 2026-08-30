using CompanioNation.Shared;

namespace CompanioNationAPI;

/// <summary>
/// Abstraction for sending push notifications.
/// Implementations handle specific transports (Web Push VAPID, FCM, etc.).
/// </summary>
public interface IPushService
{
    /// <summary>
    /// Sends a push notification using the stored push token.
    /// Returns true if the token should be kept (delivery succeeded, or the failure
    /// is transient/payload-related), false only when the token is stale and should
    /// be cleared.
    /// </summary>
    Task<bool> SendAsync(string pushToken, SendMessageResult messageParameters);

    /// <summary>
    /// Sends a generic push notification (title/body/url/badge) used for admin
    /// broadcast and targeted sends. Same keep/clear token semantics as the
    /// message-specific overload.
    /// </summary>
    Task<bool> SendAsync(string pushToken, PushPayload payload);
}
