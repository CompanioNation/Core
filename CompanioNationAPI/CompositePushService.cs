using CompanioNation.Shared;

namespace CompanioNationAPI;

/// <summary>
/// Routes push notifications to the correct transport (Web Push VAPID or FCM)
/// based on the stored push token format.
/// Web Push tokens are JSON objects: {"endpoint":"...","keys":{...}}
/// FCM tokens are opaque strings (no JSON structure).
/// </summary>
public class CompositePushService : IPushService
{
    private readonly VapidPushService _vapidPushService;
    private readonly FcmPushService _fcmPushService;

    public CompositePushService(VapidPushService vapidPushService, FcmPushService fcmPushService)
    {
        _vapidPushService = vapidPushService;
        _fcmPushService = fcmPushService;
    }

    public Task<bool> SendAsync(string pushToken, SendMessageResult messageParameters)
    {
        if (string.IsNullOrWhiteSpace(pushToken))
            return Task.FromResult(false);

        // Web Push subscription tokens are JSON objects starting with '{'
        // FCM device tokens are plain opaque strings
        if (pushToken.TrimStart().StartsWith('{'))
        {
            return _vapidPushService.SendAsync(pushToken, messageParameters);
        }

        return _fcmPushService.SendAsync(pushToken, messageParameters);
    }
}
