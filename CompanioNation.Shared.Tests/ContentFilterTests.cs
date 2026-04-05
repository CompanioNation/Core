using CompanioNationAPI;

namespace CompanioNation.Shared.Tests;

public class ContentFilterTests
{
    [Fact]
    public void WhenTextIsNullThenReturnsFalse()
    {
        Assert.False(ContentFilter.ContainsProhibitedContent(null));
    }

    [Fact]
    public void WhenTextIsEmptyThenReturnsFalse()
    {
        Assert.False(ContentFilter.ContainsProhibitedContent(""));
    }

    [Fact]
    public void WhenTextIsWhitespaceThenReturnsFalse()
    {
        Assert.False(ContentFilter.ContainsProhibitedContent("   "));
    }

    [Fact]
    public void WhenTextIsCleanThenReturnsFalse()
    {
        Assert.False(ContentFilter.ContainsProhibitedContent("Hello, how are you doing today?"));
    }

    [Fact]
    public void WhenTextContainsSlurThenReturnsTrue()
    {
        Assert.True(ContentFilter.ContainsProhibitedContent("you are a faggot"));
    }

    [Fact]
    public void WhenTextContainsSlurUppercaseThenReturnsTrue()
    {
        Assert.True(ContentFilter.ContainsProhibitedContent("YOU ARE A FAGGOT"));
    }

    [Fact]
    public void WhenTextContainsSlurMixedCaseThenReturnsTrue()
    {
        Assert.True(ContentFilter.ContainsProhibitedContent("you are a FaGgOt"));
    }

    [Fact]
    public void WhenTextContainsHateSpeechPhraseThenReturnsTrue()
    {
        Assert.True(ContentFilter.ContainsProhibitedContent("white power forever"));
    }

    [Fact]
    public void WhenTextContainsThreatThenReturnsTrue()
    {
        Assert.True(ContentFilter.ContainsProhibitedContent("i will kill you if you don't stop"));
    }

    [Fact]
    public void WhenTextContainsExplicitSolicitationThenReturnsTrue()
    {
        Assert.True(ContentFilter.ContainsProhibitedContent("send nudes please"));
    }

    [Theory]
    [InlineData("I went to Scunthorpe last weekend")]
    [InlineData("She was holding a cocktail")]
    [InlineData("The word classic is fine")]
    [InlineData("The therapist helped me")]
    [InlineData("We need to assess the situation")]
    public void WhenPartialWordMatchThenReturnsFalse(string text)
    {
        Assert.False(ContentFilter.ContainsProhibitedContent(text));
    }

    [Fact]
    public void WhenSanitizeCleanTextThenReturnsUnchanged()
    {
        string input = "Hello, how are you?";
        Assert.Equal(input, ContentFilter.Sanitize(input));
    }

    [Fact]
    public void WhenSanitizeProhibitedTextThenReplacesWithAsterisks()
    {
        string result = ContentFilter.Sanitize("you are a faggot and a loser");
        Assert.DoesNotContain("faggot", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***", result);
        Assert.Contains("loser", result);
    }

    [Fact]
    public void WhenSanitizeNullThenReturnsEmptyString()
    {
        Assert.Equal("", ContentFilter.Sanitize(null));
    }

    [Fact]
    public void WhenSanitizeEmptyThenReturnsEmptyString()
    {
        Assert.Equal("", ContentFilter.Sanitize(""));
    }
}
