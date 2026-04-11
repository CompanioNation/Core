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

    public async Task<bool> SendAsync(string pushToken, SendMessageResult messageParameters)
    {
        if (_messaging == null)
        {
            await ErrorLog.LogErrorMessage(
                "FCM push notification skipped — Firebase is not configured. " +
                "Set the FCM_SERVICE_ACCOUNT_JSON environment variable.");
            return false;
        }

        var message = new Message
        {
            Token = pushToken,
            Notification = new Notification
            {
                Title = messageParameters.FromUserName,
                Body = messageParameters.MessageText
            },
            Data = new Dictionary<string, string>
            {
                ["url"] = $"/Messages/{messageParameters.FromUserId}",
                ["userId"] = messageParameters.FromUserId.ToString(),
                ["tag"] = "new_message"
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Badge = 1,
                    Sound = "default"
                }
            }
        };

        try
        {
            await _messaging.SendAsync(message);
            return true;
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
            ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
        {
            // Token is invalid or device unregistered — caller should clear it
            Console.WriteLine($"FCM token invalid/unregistered: {ex.Message}");
            return false;
        }
        catch (FirebaseMessagingException ex)
        {
            Console.WriteLine($"FCM push notification error: {ex.MessagingErrorCode} — {ex.Message}");
            return false;
        }
    }
}
