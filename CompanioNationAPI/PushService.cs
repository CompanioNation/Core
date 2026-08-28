using System.Net;
using System.Text.Json;
using WebPush;
using CompanioNation.Shared;

namespace CompanioNationAPI
{
    /// <summary>
    /// Web Push (VAPID) implementation of <see cref="IPushService"/>.
    /// Handles browser-based push subscriptions (PWA / Android / desktop).
    /// </summary>
    public class VapidPushService : IPushService
    {
        private readonly VapidDetails _vapidDetails;

        public VapidPushService()
        {
            // Load private key from environment variable (MUST be secret)
            var vapidPrivateKey = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY") 
                ?? throw new InvalidOperationException("VAPID_PRIVATE_KEY environment variable is not set.");
            
            // Use public key from shared constants
            _vapidDetails = new VapidDetails(
                "mailto:info@companionation.com",
                Util.VapidPublicKey,  // From shared project
                vapidPrivateKey       // From environment variable
            );
        }

        public async Task<bool> SendAsync(string subscription, SendMessageResult messageParameters)
        {
            PushSubscriptionModel pushSubscription;
            
            try
            {
                pushSubscription = JsonSerializer.Deserialize<PushSubscriptionModel>(subscription);

                if (pushSubscription == null)
                {
                    Console.WriteLine("Subscription deserialized to null.");
                    return false;
                }
            }
            catch (ArgumentNullException ex)
            {
                Console.WriteLine($"ArgumentNullException: {ex.Message}");
                return false;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JsonException: {ex.Message}");
                return false;
            }

            var webPushClient = new WebPushClient();

            var payload = new
            {
                title = messageParameters.FromUserName,
                options = new
                {
                    body = PushNotificationText.Truncate(messageParameters.MessageText),
                    icon = "/favicon.png",
                    badge = "/cn_badge.png",
                    tag = "new_message",
                    renotify = true,
                    data = new
                    {
                        url = $"/Messages/{messageParameters.FromUserId}",
                        userId = messageParameters.FromUserId
                    }
                }
            };

            string jsonPayload = JsonSerializer.Serialize(payload);

            try
            {
                PushSubscription p = new PushSubscription(pushSubscription.Endpoint, pushSubscription.Keys.P256dh, pushSubscription.Keys.Auth);
                await webPushClient.SendNotificationAsync(p, jsonPayload, _vapidDetails);
                return true;
            }
            catch (WebPushException ex)
            {
                Console.WriteLine($"Push notification error: {ex.Message}");

                // Only 404/410 mean the subscription no longer exists and the
                // stored token should be cleared. Other failures (payload too large,
                // transient network issues, etc.) must not invalidate a valid
                // subscription.
                return !(ex.StatusCode == HttpStatusCode.NotFound ||
                         ex.StatusCode == HttpStatusCode.Gone);
            }
        }
    }
}
