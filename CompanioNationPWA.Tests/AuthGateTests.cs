using CompanioNation.Shared;
using CompanioNationPWA.Layout;
using CompanioNationPWA.Pages;

namespace CompanioNationPWA.Tests;

public class AuthGateTests : UiTestBase
{
    private static UserDetails CreateUser(bool completeProfile, int? acceptedTermsVersion)
    {
        var user = new UserDetails
        {
            LoginToken = Guid.NewGuid(),
            UserId = 42,
            Name = completeProfile ? "Test User" : string.Empty,
            Email = "test@example.com",
            Description = completeProfile ? "A test profile" : string.Empty,
            Gender = completeProfile ? 2 : null,
            DateOfBirth = completeProfile ? DateTime.Today.AddYears(-25) : null,
            Geonameid = completeProfile ? 1 : 0,
            CityDisplayName = "Test City",
            Thumbnail = completeProfile ? Guid.NewGuid() : Guid.Empty,
            AcceptedTermsVersion = acceptedTermsVersion,
            Verified = true,
        };

        return user;
    }

    [Fact]
    public void WhenNotLoggedInThenMainLayoutShowsLandingPage()
    {
        SignalRClient.CurrentUser = null;

        var cut = Context.Render<MainLayout>();

        cut.WaitForAssertion(() => Assert.Contains("LandingPage_Headline", cut.Markup));
    }

    [Fact]
    public void WhenTermsNotAcceptedThenMainLayoutShowsTerms()
    {
        SignalRClient.CurrentUser = CreateUser(completeProfile: false, acceptedTermsVersion: null);

        var cut = Context.Render<MainLayout>();

        cut.WaitForAssertion(() => Assert.Contains("Terms_Title", cut.Markup));
    }

    [Fact]
    public void WhenBasicInfoMissingThenMainLayoutShowsEnterBasicInfo()
    {
        SignalRClient.CurrentUser = CreateUser(completeProfile: false, acceptedTermsVersion: 1);

        var cut = Context.Render<MainLayout>();

        cut.WaitForAssertion(() => Assert.Contains("EnterBasicInfo_SaveProfile", cut.Markup));
    }

    [Fact]
    public void WhenProfileCompleteThenMainLayoutShowsNavigation()
    {
        SignalRClient.CurrentUser = CreateUser(completeProfile: true, acceptedTermsVersion: 1);

        var cut = Context.Render<MainLayout>();

        cut.WaitForAssertion(() => Assert.Contains("MainLayout_NavAdvice", cut.Markup));
    }

    [Fact]
    public void WhenProfileCompleteButUnverifiedThenMainLayoutShowsCheckEmail()
    {
        SignalRClient.CurrentUser = CreateUser(completeProfile: true, acceptedTermsVersion: 1);
        SignalRClient.CurrentUser.Verified = false;

        var cut = Context.Render<MainLayout>();

        cut.WaitForAssertion(() => Assert.Contains("MainLayout_CheckEmailTitle", cut.Markup));
    }

    [Fact]
    public async Task WhenTermsAcceptedThenCurrentUserVersionIsUpdated()
    {
        var accepted = false;
        SignalRClient.CurrentUser = CreateUser(completeProfile: false, acceptedTermsVersion: null);

        var cut = Context.Render<Terms>(p => p.Add(x => x.OnTermsAccepted, () => accepted = true));

        await cut.Find("button.blueButton").ClickAsync();

        Assert.True(accepted);
        Assert.Equal(Terms.CURRENT_TERMS_VERSION, SignalRClient.CurrentUser?.AcceptedTermsVersion);
    }

    [Fact]
    public async Task WhenTermsAcceptFailsThenErrorIsShown()
    {
        SignalRClient.CurrentUser = CreateUser(completeProfile: false, acceptedTermsVersion: null);
        SignalRClient.AcceptTermsAsyncHandler = _ =>
            Task.FromResult(ResponseWrapper<bool>.Fail(1, "Terms_Failed"));

        var cut = Context.Render<Terms>();

        await cut.Find("button.blueButton").ClickAsync();

        Assert.Contains("Terms_Failed", cut.Markup);
    }

    [Fact]
    public async Task WhenLoginPopupHiddenThenShowLoginRevealsEmailForm()
    {
        var cut = Context.Render<Login>();

        Assert.True(cut.Find("#loginPopup").HasAttribute("hidden"));

        await cut.InvokeAsync(() => cut.Instance.ShowLogin());

        Assert.False(cut.Find("#loginPopup").HasAttribute("hidden"));
        Assert.Contains("Login_Title", cut.Markup);
    }
}
