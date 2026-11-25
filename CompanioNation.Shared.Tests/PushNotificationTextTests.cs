using System.Data;
using System.Text;
using System.Text.Json;
using CompanioNationAPI;
using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class PushNotificationTextTests
{
    [Fact]
    public void WhenTextFitsThenItIsUnchanged()
    {
        var text = new string('a', 298) + "\U0001F600";

        Assert.Equal(text, PushNotificationText.Truncate(text));
    }

    [Fact]
    public void WhenTextExceedsLimitThenItKeepsTheBeginningAndEllipsis()
    {
        Assert.Equal(new string('a', 299) + "…", PushNotificationText.Truncate(new string('a', 301)));
    }

    [Fact]
    public void WhenCutFallsInsideEmojiThenWholeEmojiIsOmitted()
    {
        var text = new string('a', 298) + "\U0001F600" + "x";

        Assert.Equal(new string('a', 298) + "…", PushNotificationText.Truncate(text));
    }

    [Fact]
    public void WhenEmojiFitsBeforeCutThenWholeEmojiIsKept()
    {
        var text = new string('a', 297) + "\U0001F600" + "xx";

        Assert.Equal(new string('a', 297) + "\U0001F600…", PushNotificationText.Truncate(text));
    }

    [Fact]
    public void WhenTextIsBlankThenResultIsEmpty()
    {
        Assert.Equal(string.Empty, PushNotificationText.Truncate(null));
        Assert.Equal(string.Empty, PushNotificationText.Truncate("   "));
    }

    [Fact]
    public void WhenUnreadMessagesFitThenTheyAppearInChronologicalOrder()
    {
        Assert.Equal("older\nnewer", PushNotificationText.FormatUnreadSummary(["newer", "older"]));
        Assert.Equal(string.Empty, PushNotificationText.FormatUnreadSummary([]));
    }

    [Fact]
    public void WhenManyShortMessagesFitThenNoneAreDropped()
    {
        var messages = Enumerable.Repeat("hi", 100).ToArray();

        Assert.Equal(string.Join('\n', messages), PushNotificationText.FormatUnreadSummary(messages));
    }

    [Fact]
    public void WhenUnreadSummaryExceedsLimitThenNewestTextIsKept()
    {
        Assert.Equal("…" + new string('b', 299),
            PushNotificationText.FormatUnreadSummary([new string('b', 300), "older"]));
    }

    [Fact]
    public void WhenSummaryIsClippedThenWholeEmojiIsPreserved()
    {
        string result = PushNotificationText.FormatUnreadSummary([new string('b', 297), "aa\U0001F600"]);

        Assert.Equal("…\U0001F600\n" + new string('b', 297), result);
        Assert.Equal(PushNotificationText.MaxBodyLength, result.EnumerateRunes().Count());
    }

    [Fact]
    public async Task WhenEmojiHistoryFitsInCodePointsThenItIsNotClipped()
    {
        var table = new DataTable();
        table.Columns.Add("message_text", typeof(string));
        table.Rows.Add(string.Concat(Enumerable.Repeat("\U0001F600", 100)));
        table.Rows.Add(string.Concat(Enumerable.Repeat("\U0001F600", 60)));
        using var reader = table.CreateDataReader();

        var body = await PushNotificationText.ReadUnreadSummaryAsync(reader);

        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", 60)) + "\n" +
            string.Concat(Enumerable.Repeat("\U0001F600", 100)), body);
    }

    [Fact]
    public async Task WhenReadingUnreadMessagesThenSummaryIsBoundedAndStopsReadingOlderRows()
    {
        var table = new DataTable();
        table.Columns.Add("message_text", typeof(string));
        table.Rows.Add(new string('b', 300));
        table.Rows.Add("older");
        table.Rows.Add("not needed");
        using var reader = table.CreateDataReader();

        Assert.Equal("…" + new string('b', 299), await PushNotificationText.ReadUnreadSummaryAsync(reader));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("not needed", reader.GetString(0));
    }

    [Fact]
    public async Task WhenUnreadMessagesFitThenReaderKeepsAllOfThem()
    {
        var table = new DataTable();
        table.Columns.Add("message_text", typeof(string));
        for (int i = 0; i < 100; i++) table.Rows.Add("hi");
        using var reader = table.CreateDataReader();

        Assert.Equal(string.Join('\n', Enumerable.Repeat("hi", 100)),
            await PushNotificationText.ReadUnreadSummaryAsync(reader));
    }

    [Fact]
    public async Task WhenLegacyUnreadTextIsWhitespaceThenItStillCountsTowardTheBound()
    {
        var table = new DataTable();
        table.Columns.Add("message_text", typeof(string));
        for (int i = 0; i < 300; i++) table.Rows.Add(" ");
        using var reader = table.CreateDataReader();

        var body = await PushNotificationText.ReadUnreadSummaryAsync(reader);

        Assert.Equal(PushNotificationText.MaxBodyLength, body.Length);
        Assert.StartsWith("…", body);
        Assert.True(await reader.ReadAsync());
    }

    [Fact]
    public void WhenBuildingMessagePayloadThenItUsesTheUnreadSummaryAndLatestSender()
    {
        var message = new SendMessageResult
        {
            MessageText = "latest only",
            PushBody = "older\nlatest only",
            FromUserName = "Alice",
            FromUserId = 7,
            RecipientUnreadCount = 2
        };

        var payload = PushNotificationText.CreateMessagePayload(message);

        Assert.Equal("older\nlatest only", payload.Body);
        Assert.Equal("Alice", payload.Title);
        Assert.Equal("/Messages/7", payload.Url);
        Assert.Equal("new_message", payload.Tag);
        Assert.Equal(7, payload.UserId);
        Assert.Equal(2, payload.Badge);
        Assert.DoesNotContain("older\\nlatest only", JsonSerializer.Serialize(message));
        Assert.DoesNotContain("PushBody", JsonSerializer.Serialize(message));
    }

    [Fact]
    public void WhenEmojiSummaryFitsPushPayloadThenItIsNotTruncatedAgain()
    {
        string body = string.Concat(Enumerable.Repeat("\U0001F600", 160));
        var payload = PushNotificationText.CreateMessagePayload(new SendMessageResult
        {
            PushBody = body,
            FromUserName = "Alice",
            FromUserId = 7
        });

        Assert.Equal(body, payload.Body);
    }

    [Fact]
    public void WhenEncodedPushWouldExceedLimitThenItKeepsNewestWholeCharacters()
    {
        string body = string.Concat(Enumerable.Repeat("\U0001F600", 300));
        var payload = PushNotificationText.CreateMessagePayload(new SendMessageResult
        {
            PushBody = body,
            FromUserName = new string('\u4e00', 50),
            FromUserId = int.MaxValue
        });

        Assert.True(Encoding.UTF8.GetByteCount(PushNotificationText.SerializeWebPayload(payload)) <= 4096 - 103);
        Assert.StartsWith("…", payload.Body);
        Assert.EndsWith("\U0001F600", payload.Body);
        Assert.True(payload.Body.AsSpan(1).ToString().EnumerateRunes().All(rune => rune.Value == 0x1F600));
    }

    [Fact]
    public async Task WhenProcedureShipsAWholeMessageThenOnlyItsTailReachesTheNotification()
    {
        var table = new DataTable();
        table.Columns.Add("message_text", typeof(string));
        table.Rows.Add("HEAD" + new string('z', 400) + "TAIL");
        using var reader = table.CreateDataReader();

        var body = await PushNotificationText.ReadUnreadSummaryAsync(reader);

        Assert.StartsWith("…", body);
        Assert.EndsWith("TAIL", body);
        Assert.DoesNotContain("HEAD", body);
        Assert.Equal(PushNotificationText.MaxBodyLength, body.Length);
    }

    [Fact]
    public async Task WhenClippedRowStartsMidSurrogatePairThenTheStrayHalfIsDropped()
    {
        var table = new DataTable();
        table.Columns.Add("message_text", typeof(string));
        // RIGHT() clamps by UTF-16 length, so it can land between the halves of an emoji.
        table.Rows.Add("\uDE00hi");
        using var reader = table.CreateDataReader();

        Assert.Equal("hi", await PushNotificationText.ReadUnreadSummaryAsync(reader));
    }

    [Fact]
    public void WhenRetainedTailAlreadyStartsAtAnEllipsisThenItIsNotDoubled()
    {
        string text = new string('a', 3) + "…" + new string('b', 298);

        Assert.Equal("…" + new string('b', 298), PushNotificationText.FormatUnreadSummary([text]));
    }
}
