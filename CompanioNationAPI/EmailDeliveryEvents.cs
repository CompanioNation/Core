using CompanioNation.Shared;
using System.Text.Json;

namespace CompanioNationAPI
{
    /// <summary>One actionable delivery outcome extracted from an Event Grid payload.</summary>
    public sealed record EmailDeliveryEvent(string Recipient, EmailDeliveryState State, string? Error);

    /// <summary>
    /// Reads Azure Communication Services email events (delivered over Event Grid). Kept free
    /// of HTTP concerns so the mapping can be unit tested directly.
    /// </summary>
    public static class EmailDeliveryEventParser
    {
        public const string SubscriptionValidationEventType = "Microsoft.EventGrid.SubscriptionValidationEvent";
        public const string DeliveryReportEventType = "Microsoft.Communication.EmailDeliveryReportReceived";

        /// <summary>
        /// Returns the Event Grid subscription validation code, or null when the payload is not
        /// a validation handshake. Event Grid requires the code echoed back as
        /// <c>{"validationResponse":"..."}</c> before it will deliver any events.
        /// </summary>
        public static string? ReadValidationCode(JsonDocument document)
        {
            foreach (JsonElement element in EnumerateEvents(document))
            {
                if (!string.Equals(GetString(element, "eventType"), SubscriptionValidationEventType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!element.TryGetProperty("data", out JsonElement data))
                {
                    continue;
                }

                string? code = GetString(data, "validationCode");
                if (!string.IsNullOrWhiteSpace(code))
                {
                    return code;
                }
            }

            return null;
        }

        /// <summary>
        /// Extracts every delivery report that carries a terminal state worth recording.
        /// Events with no recipient or no actionable status (e.g. <c>Expanded</c>) are skipped.
        /// </summary>
        public static List<EmailDeliveryEvent> ParseDeliveryReports(JsonDocument document)
        {
            var results = new List<EmailDeliveryEvent>();

            foreach (JsonElement element in EnumerateEvents(document))
            {
                if (!string.Equals(GetString(element, "eventType"), DeliveryReportEventType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!element.TryGetProperty("data", out JsonElement data))
                {
                    continue;
                }

                // The recipient address is the key: it is the only thing that ties a report
                // back to an account, so a report without one is unusable.
                string? recipient = GetString(data, "recipient");
                if (string.IsNullOrWhiteSpace(recipient))
                {
                    continue;
                }

                // ACS Event Grid reports carry the status as "status"; "deliveryStatus" does
                // not exist in the schema. Reading the wrong name silently dropped EVERY
                // report, so bounces never reached the account that caused them.
                EmailDeliveryState? state = MapStatus(GetString(data, "status"));
                if (state is null)
                {
                    continue;
                }

                results.Add(new EmailDeliveryEvent(recipient.Trim(), state.Value, Describe(data)));
            }

            return results;
        }

        /// <summary>
        /// Maps an ACS delivery status to the state stored against the recipient account.
        /// Returns null for statuses that carry no actionable outcome.
        /// </summary>
        /// <remarks>
        /// Every outcome is recorded so the owner can be told. <c>Failed</c> is a soft bounce
        /// (full mailbox, temporary refusal): <see cref="EmailDeliveryState.TransientFailure"/>.
        /// <c>Quarantined</c> and <c>FilteredSpam</c> mean the mail was held back as spam, which
        /// says nothing about the address: <see cref="EmailDeliveryState.HeldAsSpam"/> asks the
        /// owner to check their spam folder and add us to their contacts, never to change the address.
        /// </remarks>
        public static EmailDeliveryState? MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
        {
            "delivered" => EmailDeliveryState.Delivered,
            "bounced" => EmailDeliveryState.PermanentFailure,
            "suppressed" => EmailDeliveryState.PermanentFailure,
            "quarantined" => EmailDeliveryState.HeldAsSpam,
            "filteredspam" => EmailDeliveryState.HeldAsSpam,
            "failed" => EmailDeliveryState.TransientFailure,
            _ => null
        };

        private static string? Describe(JsonElement data)
        {
            string status = GetString(data, "status") ?? "unknown";
            if (data.TryGetProperty("deliveryStatusDetails", out JsonElement details))
            {
                string? message = GetString(details, "statusMessage");
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return $"{status}: {message}";
                }
            }

            return status;
        }

        /// <summary>Event Grid posts an array; a bare object is accepted too so manual tests work.</summary>
        private static IEnumerable<JsonElement> EnumerateEvents(JsonDocument document)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    yield return element;
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                yield return document.RootElement;
            }
        }

        private static string? GetString(JsonElement element, string propertyName)
            => element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
    }
}
