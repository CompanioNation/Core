using CompanioNationAPI;

namespace CompanioNation.Shared.Tests;

public class LinkEmailTemplateTests
{
    [Theory]
    [InlineData("LinkInvite.html")]
    [InlineData("LinkInvite.txt")]
    [InlineData("LinkPhotoPending.html")]
    [InlineData("LinkPhotoPending.txt")]
    public void LinkTemplatesAreEmbedded(string filename)
    {
        using var stream = typeof(CompanioNationHub).Assembly.GetManifestResourceStream($"CompanioNationAPI.EmailTemplates.{filename}");
        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void InviteHtmlContainsClickableConfirmationAndRejectionLinks()
    {
        using var stream = typeof(CompanioNationHub).Assembly.GetManifestResourceStream("CompanioNationAPI.EmailTemplates.LinkInvite.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd();
        Assert.Contains("href=\"{BaseUrl}/auth/link/confirm?code={VerificationCode}\"", html);
        Assert.Contains("href=\"{BaseUrl}/auth/link/reject?code={VerificationCode}\"", html);
    }
}
