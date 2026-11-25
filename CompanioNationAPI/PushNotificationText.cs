using System.Data.Common;
using System.Text;
using System.Text.Json;
using CompanioNation.Shared;

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
    private const int MaxWebPushPlaintextBytes = 4096 - 103;

    public static PushPayload CreateMessagePayload(SendMessageResult message)
    {
        var payload = new PushPayload
        {
            Title = message.FromUserName,
            Body = message.PushBody,
            Url = $"/Messages/{message.FromUserId}",
            Badge = message.RecipientUnreadCount,
            Tag = "new_message",
            UserId = message.FromUserId
        };

        string remaining = payload.Body;
        while (Encoding.UTF8.GetByteCount(SerializeWebPayload(payload)) > MaxWebPushPlaintextBytes)
        {
            if (remaining.Length == 0)
                throw new InvalidOperationException("Message push metadata exceeds the Web Push payload limit.");
            int remove = remaining.Length > 1 && char.IsSurrogatePair(remaining[0], remaining[1]) ? 2 : 1;
            remaining = remaining[remove..];
            payload = payload with { Body = "…" + remaining };
        }
        return payload;
    }

    public static string SerializeWebPayload(PushPayload payload) => JsonSerializer.Serialize(new
    {
        title = payload.Title,
        options = new
        {
            body = payload.Body,
            icon = "/favicon.png",
            badge = "/cn_badge.png",
            tag = payload.Tag,
            renotify = true,
            data = new { url = payload.Url, userId = payload.UserId }
        }
    });

    public static string Truncate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (text.Length <= MaxBodyLength)
            return text;

        int end = MaxBodyLength - 1;
        if (char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end]))
            end--;
        return text[..end] + "…";
    }

    public static string FormatUnreadSummary(IReadOnlyList<string> newestFirst)
        => TakeTail(string.Join('\n', newestFirst.Reverse()), MaxBodyLength);

    /// <summary>
    /// Keeps the LAST <paramref name="maxLength"/> characters of <paramref name="text"/>,
    /// marking the cut with a leading ellipsis only when something was actually dropped.
    /// Counts code points so a clipped body never splits an emoji.
    /// </summary>
    private static string TakeTail(string text, int maxLength)
    {
        if (text.EnumerateRunes().Count() <= maxLength)
            return text;

        int start = text.Length;
        for (int count = 0; count < maxLength - 1; count++)
        {
            start--;
            if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1]))
                start--;
        }
        return text[start] == '…' ? text[start..] : "…" + text[start..];
    }

    public static async Task<string> ReadUnreadSummaryAsync(DbDataReader reader)
    {
        var unread = new List<string>();
        int length = 0;
        while (length <= MaxBodyLength && await reader.ReadAsync())
        {
            // cn_send_message already clipped each row to its last MaxBodyLength characters,
            // so this is normally a no-op; TakeTail still covers an app that runs ahead of the
            // deployed procedure, and a clip that landed mid surrogate pair leaves a lone low
            // surrogate at the start of the row.
            string text = TakeTail(reader.GetString(0), MaxBodyLength);
            if (text.Length > 0 && char.IsLowSurrogate(text[0])) text = text[1..];
            if (text.Length == 0) continue;
            length += text.EnumerateRunes().Count() + (unread.Count == 0 ? 0 : 1);
            unread.Add(text);
        }
        return FormatUnreadSummary(unread);
    }
}
