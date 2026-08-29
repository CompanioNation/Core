using CompanioNation.Shared;
using CompanioNationPWA.Components;
using CompanioNationPWA.Pages;
using CompanioNationPWA.Tests.Fakes;
using Microsoft.AspNetCore.Components.Forms;

namespace CompanioNationPWA.Tests;

public class EnterBasicInfoTests : UiTestBase
{
    private static UserDetails CreateCompleteUser()
    {
        return new UserDetails
        {
            LoginToken = Guid.NewGuid(),
            UserId = 42,
            Name = "Test User",
            Email = "test@example.com",
            Description = "A test profile",
            Gender = 2,
            DateOfBirth = DateTime.Today.AddYears(-25),
            Geonameid = 1,
            CityDisplayName = "Test City",
            Thumbnail = Guid.NewGuid(),
            AcceptedTermsVersion = 1,
        };
    }

    [Fact]
    public void WhenCurrentUserExistsThenFormRenders()
    {
        SignalRClient.CurrentUser = CreateCompleteUser();

        var cut = Context.Render<EnterBasicInfo>();

        cut.WaitForAssertion(() => Assert.Contains("EnterBasicInfo_SaveProfile", cut.Markup));
        Assert.Contains("EnterBasicInfo_NameLabel", cut.Markup);
        Assert.DoesNotContain("EnterBasicInfo_Loading", cut.Markup);
    }

    [Fact]
    public void WhenCurrentUserIsNullThenNavigatesHome()
    {
        SignalRClient.CurrentUser = null;
        NavigationManager.NavigateTo("/start");

        Context.Render<EnterBasicInfo>();

        Assert.Equal("http://localhost/", NavigationManager.Uri);
    }

    [Fact]
    public async Task WhenValidationFailsThenShowsErrorInsteadOfNavigating()
    {
        var invalidUser = CreateCompleteUser();
        invalidUser.Name = string.Empty;
        invalidUser.Description = string.Empty;
        invalidUser.Gender = null;
        invalidUser.DateOfBirth = null;
        invalidUser.Geonameid = 0;
        SignalRClient.CurrentUser = invalidUser;
        NavigationManager.NavigateTo("/start");

        var cut = Context.Render<EnterBasicInfo>();
        cut.WaitForAssertion(() => Assert.Contains("EnterBasicInfo_SaveProfile", cut.Markup));

        await cut.Find("button.btn-action").ClickAsync();

        Assert.Contains("EnterBasicInfo_FormErrors", cut.Markup);
        Assert.Equal("http://localhost/start", NavigationManager.Uri);
    }

    [Fact]
    public async Task WhenUpdateFailsThenShowsUpdateErrorInsteadOfThrowing()
    {
        SignalRClient.CurrentUser = CreateCompleteUser();
        SignalRClient.UpdateUserDetailsAsyncHandler = () => Task.FromResult(false);
        NavigationManager.NavigateTo("/start");

        var cut = Context.Render<EnterBasicInfo>();
        cut.WaitForAssertion(() => Assert.Contains("EnterBasicInfo_SaveProfile", cut.Markup));

        await cut.Find("button.btn-action").ClickAsync();

        Assert.Contains("EnterBasicInfo_UpdateError", cut.Markup);
        Assert.Equal("http://localhost/start", NavigationManager.Uri);
    }

    [Fact]
    public async Task WhenSubmitSucceedsThenUpdatesUserAndNavigatesHome()
    {
        SignalRClient.CurrentUser = CreateCompleteUser();
        NavigationManager.NavigateTo("/start");

        var cut = Context.Render<EnterBasicInfo>();
        cut.WaitForAssertion(() => Assert.Contains("EnterBasicInfo_SaveProfile", cut.Markup));

        await cut.Find("button.btn-action").ClickAsync();

        Assert.Equal(1, SignalRClient.UpdateUserDetailsCallCount);
        Assert.Equal("http://localhost/", NavigationManager.Uri);
    }

    [Fact]
    public async Task WhenPhotoUploadFailsThenShowsFileError()
    {
        SignalRClient.CurrentUser = CreateCompleteUser();
        SignalRClient.UploadPhotoAsyncHandler = _ => Task.FromResult((-1, Guid.Empty));

        var cut = Context.Render<EnterBasicInfo>();
        cut.WaitForAssertion(() => Assert.Contains("EnterBasicInfo_SaveProfile", cut.Markup));

        var inputFile = cut.FindComponent<InputFile>();
        inputFile.UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3, 4], "test.jpg", DateTimeOffset.UtcNow, "image/jpeg"));

        Assert.Contains("EnterBasicInfo_FileTooLarge", cut.Markup);
    }
}
