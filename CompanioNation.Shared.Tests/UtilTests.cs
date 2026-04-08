using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class UtilTests
{
    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData(0, "Unknown")]
    [InlineData(2, "Male")]
    [InlineData(4, "Female")]
    [InlineData(8, "Other")]
    [InlineData(16, "Trans Male")]
    [InlineData(32, "Trans Female")]
    [InlineData(99, "Invalid Gender")]
    [InlineData(-1, "Invalid Gender")]
    public void WhenGetGenderStringCalledThenReturnsExpectedLabel(int? gender, string expected)
    {
        string result = Util.GetGenderString(gender);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void WhenStripHtmlTagsCalledWithPlainTextThenReturnsUnchanged()
    {
        string input = "Hello, world!";

        string result = Util.StripHtmlTags(input);

        Assert.Equal("Hello, world!", result);
    }

    [Fact]
    public void WhenStripHtmlTagsCalledWithHtmlThenRemovesTags()
    {
        string input = "<p>Hello <strong>world</strong></p>";

        string result = Util.StripHtmlTags(input);

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void WhenStripHtmlTagsCalledWithStyleBlockThenRemovesStyleAndTags()
    {
        string input = "<style>.foo { color: red; }</style><p>Content</p>";

        string result = Util.StripHtmlTags(input);

        Assert.Equal("Content", result);
    }

    [Fact]
    public void WhenStripHtmlTagsCalledWithNestedHtmlThenStripsAll()
    {
        string input = "<div><ul><li>Item 1</li><li>Item 2</li></ul></div>";

        string result = Util.StripHtmlTags(input);

        Assert.Equal("Item 1Item 2", result);
    }

    [Fact]
    public void WhenGetPhotoUrlCalledWithEmptyGuidThenReturnsGenericProfile()
    {
        string result = Util.GetPhotoUrl(Guid.Empty);

        Assert.Equal("/images/generic-profile.jpg", result);
    }

    [Fact]
    public void WhenGetPhotoUrlCalledWithNoBaseUrlThenReturnsGenericProfile()
    {
        // Reset to null base URL
        Util.InitializePhotoBaseUrl(null);

        var guid = Guid.NewGuid();
        string result = Util.GetPhotoUrl(guid);

        Assert.Equal("/images/generic-profile.jpg", result);
    }

    [Fact]
    public void WhenGetPhotoUrlCalledWithValidGuidAndBaseUrlThenReturnsBlobUrl()
    {
        var guid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Util.InitializePhotoBaseUrl("https://blob.example.com/photos");

        string result = Util.GetPhotoUrl(guid);

        Assert.Equal("https://blob.example.com/photos/11111111-2222-3333-4444-555555555555.jpg", result);

        // Clean up static state
        Util.InitializePhotoBaseUrl(null);
    }

    [Fact]
    public void WhenGetPhotoUrlCalledWithTrailingSlashBaseUrlThenTrimsSlash()
    {
        var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Util.InitializePhotoBaseUrl("https://blob.example.com/photos/");

        string result = Util.GetPhotoUrl(guid);

        Assert.Equal("https://blob.example.com/photos/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jpg", result);

        Util.InitializePhotoBaseUrl(null);
    }

    [Fact]
    public void WhenInitializePhotoBaseUrlCalledWithWhitespaceThenTreatsAsNull()
    {
        Util.InitializePhotoBaseUrl("   ");

        var guid = Guid.NewGuid();
        string result = Util.GetPhotoUrl(guid);

        Assert.Equal("/images/generic-profile.jpg", result);
    }

    [Theory]
    [InlineData(2, "M")]
    [InlineData(4, "F")]
    [InlineData(8, "O")]
    [InlineData(16, "TM")]
    [InlineData(32, "TF")]
    [InlineData(null, "?")]
    [InlineData(0, "?")]
    [InlineData(99, "?")]
    public void WhenGetGenderShortStringCalledThenReturnsAbbreviatedLabel(int? gender, string expected)
    {
        string result = Util.GetGenderShortString(gender);

        Assert.Equal(expected, result);
    }
}
