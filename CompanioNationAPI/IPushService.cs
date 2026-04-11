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
    /// Returns true if delivery succeeded, false if the token is stale and should be cleared.
    /// </summary>
    Task<bool> SendAsync(string pushToken, SendMessageResult messageParameters);
}
