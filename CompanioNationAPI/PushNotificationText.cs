namespace CompanioNationAPI;

/// <summary>
/// Shared shaping for push notification payloads.
/// </summary>
internal static class PushNotificationText
{
    /// <summary>
    /// Web Push (VAPID) and FCM both reject notification payloads larger than 4 KB.
    /// CompanioNita advice messages can be several KB, so the notification body is
    /// truncated here; the full text remains available in the in-app message thread.
    /// </summary>
    public const int MaxBodyLength = 300;

    public static string Truncate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text.Length <= MaxBodyLength
            ? text
            : text[..(MaxBodyLength - 1)] + "…";
    }
}
