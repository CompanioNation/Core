namespace CompanioNation.Shared;

/// <summary>
/// SEO-CRITICAL: Encodes the rule that the app's loading overlay must never be
/// rendered during server-side (SSR) prerendering.
///
/// During SSR, <c>OnAfterRenderAsync</c> never runs, so any "loading" flag that
/// is only cleared there would render a permanent spinner to search-engine
/// crawlers and JavaScript-disabled visitors — hiding the exact prerendered
/// content they came to index. The decision must therefore be made directly in
/// the render path and must not depend on lifecycle-method ordering.
///
/// See MainLayout.razor (the loading gate) and PrerenderingGateTests.
/// </summary>
public static class PrerenderingGate
{
    public static bool ShouldShowLoadingOverlay(bool isLoading, bool isPrerendering)
        => isLoading && !isPrerendering;
}
