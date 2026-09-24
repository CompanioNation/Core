using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class SecureUrlPayloadTests
{
    // 32 zero bytes, base64-encoded — a stable, obviously-fake test secret.
    private const string Secret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static readonly string OtherSecret = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(i + 1)).ToArray());

    private const string Purpose = "TEST_HANDOFF|v1";

    [Fact]
    public void WhenRoundTripThenReturnsOriginalPayload()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, Secret, out string? payload);

        Assert.True(ok);
        Assert.Equal("user@example.com", payload);
    }

    [Fact]
    public void WhenPayloadInUrlBlobIsNotReadableInPlaintext()
    {
        const string email = "user@example.com";
        string blob = SecureUrlPayload.Create(email, Purpose, Secret);

        // The whole point: the email must not appear in the (URL-safe) blob.
        Assert.DoesNotContain("user@example.com", blob);
        Assert.DoesNotContain("example", blob);
    }

    [Fact]
    public void WhenAssociatedDataMatchesThenAccepted()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret, associatedData: "code-123");

        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, Secret, out string? payload, associatedData: "code-123");

        Assert.True(ok);
        Assert.Equal("user@example.com", payload);
    }

    [Fact]
    public void WhenAssociatedDataDiffersThenRejected()
    {
        // A blob minted for one authorization code must not be openable with another.
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret, associatedData: "code-123");

        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, Secret, out _, associatedData: "code-999");

        Assert.False(ok);
    }

    [Fact]
    public void WhenDifferentPurposeThenRejected()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        bool ok = SecureUrlPayload.TryOpen(blob, "OTHER_FEATURE|v1", Secret, out _);

        Assert.False(ok);
    }

    [Fact]
    public void WhenDifferentSecretThenRejected()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, OtherSecret, out _);

        Assert.False(ok);
    }

    [Fact]
    public void WhenTamperedThenRejected()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        // Flip one character of the base64url blob.
        int index = blob.Length / 2;
        char original = blob[index];
        char replacement = original == 'A' ? 'B' : 'A';
        string tampered = blob[..index] + replacement + blob[(index + 1)..];

        bool ok = SecureUrlPayload.TryOpen(tampered, Purpose, Secret, out _);

        Assert.False(ok);
    }

    [Fact]
    public void WhenExpiredThenRejected()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        // Evaluate the blob well past the default 5-minute lifetime (deterministic clock).
        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, Secret, out _, utcNow: DateTime.UtcNow.AddMinutes(10));

        Assert.False(ok);
    }

    [Fact]
    public void WhenWithinLifetimeThenAccepted()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, Secret, out string? payload, utcNow: DateTime.UtcNow.AddMinutes(4));

        Assert.True(ok);
        Assert.Equal("user@example.com", payload);
    }

    [Fact]
    public void WhenSecretNotValidBase64ThenRejectedNotThrown()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        // A misconfigured secret must fail closed, never throw out of the login path.
        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, "!!!not-base64!!!", out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]
    public void WhenBlobMalformedThenRejected(string? blob)
    {
        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, Secret, out _);

        Assert.False(ok);
    }

    [Fact]
    public void WhenSecretMissingThenRejected()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        bool ok = SecureUrlPayload.TryOpen(blob, Purpose, string.Empty, out _);

        Assert.False(ok);
    }

    [Fact]
    public void WhenCreatedThenOutputIsUrlSafe()
    {
        string blob = SecureUrlPayload.Create("user@example.com", Purpose, Secret);

        Assert.DoesNotContain('+', blob);
        Assert.DoesNotContain('/', blob);
        Assert.DoesNotContain('=', blob);
    }
}
