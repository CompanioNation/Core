using CompanioNation.Shared;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace CompanioNationAPI;

/// <summary>
/// FCM (Firebase Cloud Messaging) implementation of <see cref="IPushService"/>.
/// Handles push notifications for native iOS/Android apps that register an FCM device token.
/// </summary>
public class FcmPushService : IPushService
{
    private readonly FirebaseMessaging? _messaging;

    public FcmPushService()
    {
        // FirebaseApp is a singleton — initialize only once.
        // The FCM_SERVICE_ACCOUNT_JSON env var contains the Firebase service account JSON directly.
        if (FirebaseApp.DefaultInstance == null)
        {
            var json = Environment.GetEnvironmentVariable("FCM_SERVICE_ACCOUNT_JSON");
            if (!string.IsNullOrWhiteSpace(json))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromJson(json)
                });
            }
            else
            {
                _ = ErrorLog.LogErrorMessage(
                    "FCM push notifications DISABLED — FCM_SERVICE_ACCOUNT_JSON environment variable is not set. " +
                    "Native iOS/Android push notifications will not be sent. " +
                    "Set FCM_SERVICE_ACCOUNT_JSON to the Firebase service account JSON content " +
                    "(from Firebase Console → Project Settings → Service Accounts → Generate New Private Key).");
                return;
            }
        }

        _messaging = FirebaseMessaging.DefaultInstance;
    }

    public Task<bool> SendAsync(string pushToken, SendMessageResult messageParameters)
    {
        var payload = new PushPayload
        {
            Title = messageParameters.FromUserName,
            Body = messageParameters.MessageText,
            Url = $"/Messages/{messageParameters.FromUserId}",
            Badge = messageParameters.RecipientUnreadCount,
            Tag = "new_message",
            UserId = messageParameters.FromUserId
        };

        return SendPayloadAsync(pushToken, payload);
    }

    public Task<bool> SendAsync(string pushToken, PushPayload payload)
    {
        return SendPayloadAsync(pushToken, payload);
    }

    private async Task<bool> SendPayloadAsync(string pushToken, PushPayload messagePayload)
    {
        if (_messaging == null)
        {
            // Missing configuration is NOT a stale token — keep the token so it
            // isn't cleared while Firebase is temporarily misconfigured.
            await ErrorLog.LogErrorMessage(
                "FCM push notification skipped — Firebase is not configured. " +
                "Set the FCM_SERVICE_ACCOUNT_JSON environment variable.");
            return true;
        }

        var data = new Dictionary<string, string>
        {
            ["url"] = messagePayload.Url,
            ["tag"] = messagePayload.Tag
        };
        if (messagePayload.UserId.HasValue)
        {
            data["userId"] = messagePayload.UserId.Value.ToString();
        }

        var aps = new Aps
        {
            Sound = "default"
        };
        // Only set the numeric badge when the payload specifies one; promotional
        // sends leave it untouched so they don't clobber the unread-message count.
        if (messagePayload.Badge.HasValue)
        {
            aps.Badge = messagePayload.Badge.Value;
        }

        var message = new Message
        {
            Token = pushToken,
            Notification = new Notification
            {
                Title = messagePayload.Title,
                Body = PushNotificationText.Truncate(messagePayload.Body)
            },
            Data = data,
            Apns = new ApnsConfig
            {
                Aps = aps
            }
        };

        try
        {
            await _messaging.SendAsync(message);
            return true;
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
            ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument ||
            ex.MessagingErrorCode == MessagingErrorCode.SenderIdMismatch)
        {
            // Token is invalid/unregistered — caller should clear it.
            Console.WriteLine($"FCM token invalid/unregistered: {ex.Message}");
            return false;
        }
        catch (FirebaseMessagingException ex)
        {
            // Transient/payload/config errors (Unavailable, Internal, QuotaExceeded,
            // ThirdPartyAuthError, etc.) must NOT invalidate a valid token.
            Console.WriteLine($"FCM push notification error (transient): {ex.MessagingErrorCode} — {ex.Message}");
            return true;
        }
    }
}
