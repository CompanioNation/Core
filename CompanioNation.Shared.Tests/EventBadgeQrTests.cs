using System.Security.Cryptography;
using System.Text;
using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class EventBadgeQrTests
{
    // 32 zero bytes, base64-encoded — a stable, obviously-fake test secret.
    private const string Secret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static long UnixAt(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds();

    [Fact]
    public void WhenRoundTripThenDecodesPayload()
    {
        long ts = UnixAt(Now);
        string code = EventBadgeQr.Create(42, 7, ts, Secret);

        bool ok = EventBadgeQr.TryDecode(code, Secret, Now, out int badgeId, out int issuerUserId, out int errorCode);

        Assert.True(ok);
        Assert.Equal(42, badgeId);
        Assert.Equal(7, issuerUserId);
        Assert.Equal(ErrorCodes.Success, errorCode);
    }

    [Fact]
    public void WhenAdminOriginThenIssuerIsZero()
    {
        long ts = UnixAt(Now);
        string code = EventBadgeQr.Create(5, 0, ts, Secret);

        bool ok = EventBadgeQr.TryDecode(code, Secret, Now, out _, out int issuerUserId, out _);

        Assert.True(ok);
        Assert.Equal(0, issuerUserId);
    }

    [Fact]
    public void WhenWithinExpiryWindowThenStillValid()
    {
        long ts = UnixAt(Now);
        string code = EventBadgeQr.Create(1, 1, ts, Secret);

        // Exactly at the edge of the window.
        bool ok = EventBadgeQr.TryDecode(code, Secret, Now.AddSeconds(EventBadgeQr.ValiditySeconds), out _, out _, out _);

        Assert.True(ok);
    }

    [Fact]
    public void WhenExpiredThenReportsExpired()
    {
        long ts = UnixAt(Now);
        string code = EventBadgeQr.Create(1, 1, ts, Secret);

        bool ok = EventBadgeQr.TryDecode(code, Secret, Now.AddSeconds(EventBadgeQr.ValiditySeconds + 1), out _, out _, out int errorCode);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.BadgeQrExpired, errorCode);
    }

    [Fact]
    public void WhenSignatureTamperedThenInvalid()
    {
        long ts = UnixAt(Now);
        string code = EventBadgeQr.Create(1, 1, ts, Secret);

        // Flip the last character of the base64url payload.
        char last = code[^1];
        char replaced = last == 'A' ? 'B' : 'A';
        string tampered = code[..^1] + replaced;

        bool ok = EventBadgeQr.TryDecode(tampered, Secret, Now, out _, out _, out int errorCode);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.BadgeQrInvalid, errorCode);
    }

    [Fact]
    public void WhenDecodedWithWrongSecretThenInvalid()
    {
        long ts = UnixAt(Now);
        string code = EventBadgeQr.Create(1, 1, ts, Secret);
        string otherSecret = Convert.ToBase64String(new byte[32].Select(b => (byte)(b + 1)).ToArray());

        bool ok = EventBadgeQr.TryDecode(code, otherSecret, Now, out _, out _, out int errorCode);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.BadgeQrInvalid, errorCode);
    }

    [Fact]
    public void WhenLinkNamespacedTokenThenRejected()
    {
        // A token whose signature was computed over the LINK namespace must NOT
        // validate as a badge token even though the same secret is used.
        long ts = UnixAt(Now);
        string linkStyle = LinkNamespacedCode(42, 7, ts, Secret);

        bool ok = EventBadgeQr.TryDecode(linkStyle, Secret, Now, out _, out _, out int errorCode);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.BadgeQrInvalid, errorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!")]
    public void WhenCodeMalformedThenInvalid(string? code)
    {
        bool ok = EventBadgeQr.TryDecode(code, Secret, Now, out _, out _, out int errorCode);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.BadgeQrInvalid, errorCode);
    }

    [Fact]
    public void WhenSecretMissingThenInvalid()
    {
        string code = EventBadgeQr.Create(1, 1, UnixAt(Now), Secret);

        bool ok = EventBadgeQr.TryDecode(code, string.Empty, Now, out _, out _, out _);

        Assert.False(ok);
    }

    // Builds a payload signed with the LINK signature namespace ("LINK|...") to prove
    // the badge verifier is domain-separated from the LINK feature.
    private static string LinkNamespacedCode(int badgeId, int issuerUserId, long ts, string base64Secret)
    {
        string dataToSign = $"LINK|{badgeId}|{issuerUserId}|{ts}";
        using var hmac = new HMACSHA256(Convert.FromBase64String(base64Secret));
        string signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign))).ToLowerInvariant();

        string json = $"{{\"bid\":{badgeId},\"uid\":{issuerUserId},\"ts\":{ts},\"sig\":\"{signature}\"}}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
