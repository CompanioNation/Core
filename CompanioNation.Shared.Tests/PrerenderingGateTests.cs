using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class PrerenderingGateTests
{
    [Fact]
    public void WhenPrerenderingThenLoadingOverlayIsHidden()
    {
        // Regression guard: during SSR prerendering the loading overlay must be
        // suppressed even when isLoading is still true, otherwise crawlers and
        // JS-disabled users get a permanent spinner instead of indexed content.
        Assert.False(PrerenderingGate.ShouldShowLoadingOverlay(isLoading: true, isPrerendering: true));
    }

    [Fact]
    public void WhenLoadingAndNotPrerenderingThenLoadingOverlayIsShown()
    {
        Assert.True(PrerenderingGate.ShouldShowLoadingOverlay(isLoading: true, isPrerendering: false));
    }

    [Fact]
    public void WhenNotLoadingThenLoadingOverlayIsHidden()
    {
        Assert.False(PrerenderingGate.ShouldShowLoadingOverlay(isLoading: false, isPrerendering: false));
    }
}
