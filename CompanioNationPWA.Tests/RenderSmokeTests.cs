using CompanioNation.Shared;
using CompanioNationPWA.Components;
using CompanioNationPWA.Layout;
using CompanioNationPWA.Pages;
using Microsoft.AspNetCore.Components;
using SettingsPage = CompanioNationPWA.Pages.Settings;

namespace CompanioNationPWA.Tests;

public class RenderSmokeTests : UiTestBase
{
    private static UserDetails CreateUser(bool isAdministrator = false)
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
            IsAdministrator = isAdministrator,
        };
    }

    [Fact]
    public void WhenHomeRenderedThenShowsNewConversationButton()
    {
        var cut = Context.Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("Home_NewConversation", cut.Markup));
    }

    [Fact]
    public void WhenMessagesRenderedThenShowsConversationsHeading()
    {
        var cut = Context.Render<Messages>(p =>
            p.AddCascadingValue<MainLayout>(new MainLayout()));

        cut.WaitForAssertion(() => Assert.Contains("Messages_Conversations", cut.Markup));
    }

    [Fact]
    public void WhenFindCompanionRenderedThenShowsSearchTitle()
    {
        var cut = Context.Render<FindCompanion>();

        cut.WaitForAssertion(() => Assert.Contains("FindCompanion_Title", cut.Markup));
    }

    [Fact]
    public void WhenLinkRenderedThenShowsIntroHeading()
    {
        var cut = Context.Render<Link>();

        cut.WaitForAssertion(() => Assert.Contains("Link_IntroHeading", cut.Markup));
    }

    [Fact]
    public void WhenSettingsRenderedWithUserThenShowsTitle()
    {
        SignalRClient.CurrentUser = CreateUser();

        var cut = Context.Render<SettingsPage>();

        cut.WaitForAssertion(() => Assert.Contains("Settings_Title", cut.Markup));
    }

    [Fact]
    public void WhenPrivacyPolicyRenderedThenShowsHeading()
    {
        var cut = Context.Render<PrivacyPolicy>();

        Assert.Contains("PrivacyPolicy_Heading", cut.Markup);
    }

    [Fact]
    public void WhenResetPasswordRenderedWithoutCodeThenShowsEmailStep()
    {
        var cut = Context.Render<ResetPassword>();

        cut.WaitForAssertion(() => Assert.Contains("ResetPassword_VerifyTitle", cut.Markup));
    }

    [Fact]
    public void WhenResetPasswordRenderedWithCodeThenShowsPasswordStep()
    {
        var uri = NavigationManager.GetUriWithQueryParameter("verification_code", "some-code");
        NavigationManager.NavigateTo(uri);

        var cut = Context.Render<ResetPassword>();

        cut.WaitForAssertion(() => Assert.Contains("ResetPassword_PickPassword", cut.Markup));
    }

    [Fact]
    public void WhenCompanioNitasCornerRenderedThenShowsTitle()
    {
        var cut = Context.Render<CompanioNitasCorner>();

        Assert.Contains("CompanioNitasCorner_Title", cut.Markup);
    }

    [Fact]
    public void WhenContactRenderedThenShowsIntro()
    {
        var cut = Context.Render<Contact>();

        Assert.Contains("Contact_IntroTitle", cut.Markup);
    }

    [Fact]
    public void WhenGuaranteeRenderedThenShowsDeprecationNotice()
    {
        var cut = Context.Render<Guarantee>();

        cut.WaitForAssertion(() => Assert.Contains("Guarantee_MovedHeading", cut.Markup));
    }

    [Fact]
    public void WhenViewCompanionRenderedThenShowsTitle()
    {
        var cut = Context.Render<ViewCompanion>();

        Assert.Contains("ViewCompanion_Title", cut.Markup);
    }

    [Fact]
    public void WhenAdminRenderedAsAdministratorThenShowsPanel()
    {
        SignalRClient.CurrentUser = CreateUser(isAdministrator: true);

        var cut = Context.Render<Admin>();

        cut.WaitForAssertion(() => Assert.Contains("Administrator Panel", cut.Markup));
    }

    [Fact]
    public void WhenTestPageRenderedThenShowsTitle()
    {
        var cut = Context.Render<Test>();

        Assert.Contains("Test_Title", cut.Markup);
    }

    [Fact]
    public void WhenLandingPageRenderedThenShowsHeadline()
    {
        var cut = Context.Render<LandingPage>(p =>
            p.Add(x => x.MainLayoutInstance, new MainLayout()));

        cut.WaitForAssertion(() => Assert.Contains("LandingPage_Headline", cut.Markup));
    }

    [Fact]
    public void WhenCompanioNationLogoRenderedThenShowsTagline()
    {
        var cut = Context.Render<CompanioNationLogo>();

        cut.WaitForAssertion(() => Assert.Contains("CompanioNationLogo_Tagline", cut.Markup));
    }

    [Fact]
    public void WhenFooterRenderedThenShowsHomeLink()
    {
        var cut = Context.Render<Footer>();

        Assert.Contains("Footer_Home", cut.Markup);
    }

    [Fact]
    public void WhenFeedbackButtonRenderedThenShowsFeedbackButton()
    {
        var cut = Context.Render<FeedbackButton>();

        Assert.Contains("Feedback_SendFeedback", cut.Markup);
    }

    [Fact]
    public void WhenInformationRenderedThenShowsContent()
    {
        var cut = Context.Render<Information>(p =>
            p.Add(x => x.Content, new MarkupString("<p>info-content</p>")));

        Assert.Contains("info-content", cut.Markup);
    }

    [Fact]
    public void WhenSupportCardRenderedThenShowsTitle()
    {
        var cut = Context.Render<SupportCard>();

        Assert.Contains("SupportCard_Title", cut.Markup);
    }
}
