using CompanioNationPWA.Components;
using CitySelectComponent = CompanioNationPWA.CitySelect.CitySelect;

namespace CompanioNationPWA.Tests;

public class ComponentTests : UiTestBase
{
    [Fact]
    public void WhenChildThrowsThenErrorBoundaryShowsErrorContent()
    {
        var cut = Context.Render<CustomErrorBoundary>(p =>
            p.AddChildContent(_ => throw new InvalidOperationException("boom")));

        cut.WaitForAssertion(() => Assert.Contains("Error_Heading", cut.Markup));
        Assert.Contains("boom", cut.Markup);
    }

    [Fact]
    public async Task WhenActionButtonOnClickSucceedsThenSuccessStateIsShown()
    {
        var callbackInvoked = false;
        var cut = Context.Render<ActionButton>(p => p
            .Add(x => x.OnClick, () => callbackInvoked = true)
            .AddChildContent("Save"));

        var click = cut.InvokeAsync(() => cut.Instance.TriggerClick());

        cut.WaitForAssertion(() => Assert.True(callbackInvoked));
        cut.WaitForAssertion(() => Assert.Contains("success", cut.Markup));

        await click;
    }

    [Fact]
    public async Task WhenActionButtonOnClickThrowsThenErrorStateIsShown()
    {
        var cut = Context.Render<ActionButton>(p => p
            .Add(x => x.OnClick, () => throw new InvalidOperationException("boom"))
            .AddChildContent("Save"));

        var click = cut.InvokeAsync(() => cut.Instance.TriggerClick());

        cut.WaitForAssertion(() => Assert.Contains("error", cut.Markup));

        await click;
    }

    [Fact]
    public async Task WhenCitySelectToggledThenDropdownOpens()
    {
        var cut = Context.Render<CitySelectComponent>();

        cut.WaitForAssertion(() => Assert.Contains("CitySelect_SelectCity", cut.Markup));

        await cut.Find(".dropdown-display").ClickAsync();

        Assert.Contains("CitySelect_SelectYourCity", cut.Markup);
    }

    [Fact]
    public async Task WhenLocationIsDeniedThenCitySelectShowsDeniedMessage()
    {
        var cut = Context.Render<CitySelectComponent>();

        cut.WaitForAssertion(() => Assert.Contains("CitySelect_SelectCity", cut.Markup));
        await cut.Find(".dropdown-display").ClickAsync();
        await cut.Find(".use-my-location-button").ClickAsync();

        Assert.Contains("CitySelect_LocationDenied", cut.Markup);
    }
}
