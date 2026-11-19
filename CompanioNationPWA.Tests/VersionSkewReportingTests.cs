using System.Text.Json;
using CompanioNationPWA;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CompanioNationPWA.Tests;

/// <summary>
/// Pins the VERSION-SKEW INVARIANT: an error produced BY an upgrade is never actionable, so
/// <see cref="CompanioNationSignalRClient.LogError(string, Exception?, string?)"/> must raise the
/// "new version available" toast and record one INFO breadcrumb instead of emailing the developer.
///
/// These cases cover the classification only (the part that decides email vs. toast). The
/// motivating production bug was the second case: the push alert quotes the interop failure in
/// its additional info instead of passing the exception, so a text scan of the message and the
/// exception alone let a deploy-skew artifact through to the error inbox once per stale client.
/// </summary>
public class VersionSkewReportingTests
{
    [Theory]
    // (a) Stale stable-filename JS asset — the .NET bundle is current, so the version compare
    // cannot see this; the failure text is the only evidence.
    [InlineData("The value 'window.cnPushCapability' is not a function.", null, false)]
    [InlineData("Could not find 'getOrCreatePushSubscription' ('window').", null, false)]
    // The exact production shape: stale-asset text only in the quoted additional info.
    [InlineData("[Push] Push registration FAILED despite notification permission being GRANTED.",
        "capability=available; browserFailure=none recorded; interopError=window.getOrCreatePushSubscription threw JSException: Could not find 'getOrCreatePushSubscription' ('window').",
        false)]
    public void WhenReportCarriesStaleAssetTextThenItIsVersionSkew(string message, string? additionalInfo, bool versionMismatch)
    {
        Assert.True(Exposed.IsVersionSkewReport(message, null, additionalInfo, versionMismatch));
    }

    [Fact]
    public void WhenStaleAssetTextIsOnlyInTheInnerExceptionThenItIsVersionSkew()
    {
        var ex = new InvalidOperationException(
            "outer",
            new JSException("The value 'window.isNativeIosApp' is not a function."));

        Assert.True(Exposed.IsVersionSkewReport("Client exception", ex, null, versionMismatch: false));
    }

    [Theory]
    // (b) Whole-bundle skew: contract-shaped failures from a stale bundle.
    [InlineData(typeof(HubException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(JSException))]
    public void WhenVersionMismatchAndContractShapedFailureThenItIsVersionSkew(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "shape mismatch")!;

        Assert.True(Exposed.IsVersionSkewReport("Client exception", ex, null, versionMismatch: true));
    }

    [Fact]
    public void WhenContractShapedFailureNestsInsideAnOuterExceptionThenItIsVersionSkew()
    {
        var ex = new InvalidOperationException("outer", new HubException("shape mismatch"));

        Assert.True(Exposed.IsVersionSkewReport("Client exception", ex, null, versionMismatch: true));
    }

    [Fact]
    public void WhenVersionMismatchButTheFailureIsNotContractShapedThenItIsStillReported()
    {
        // A genuine product bug that happens to land during a deploy window is still actionable;
        // only the exception kinds a contract change can actually cause are downgraded.
        var ex = new InvalidOperationException("real bug");

        Assert.False(Exposed.IsVersionSkewReport("Client exception", ex, null, versionMismatch: true));
    }

    [Fact]
    public void WhenContractShapedFailureWithoutVersionMismatchThenItIsStillReported()
    {
        var ex = new HubException("server-side failure on a matching client");

        Assert.False(Exposed.IsVersionSkewReport("Client exception", ex, null, versionMismatch: false));
    }

    [Fact]
    public void WhenReportIsOrdinaryThenItIsNotVersionSkew()
    {
        Assert.False(Exposed.IsVersionSkewReport(
            "[Push] Push subscription validation threw an unexpected exception.",
            new InvalidOperationException("boom"),
            "capability=available",
            versionMismatch: false));
    }

    [Fact]
    public void WhenEverythingIsEmptyThenItIsNotVersionSkew()
    {
        Assert.False(Exposed.IsVersionSkewReport(null, null, null, versionMismatch: false));
        Assert.False(Exposed.IsVersionSkewReport(string.Empty, null, "   ", versionMismatch: true));
    }

    /// <summary>
    /// Exposes the protected static classifier. Subclassing (rather than reflection) keeps the
    /// test compiling against the real signature, so a signature change breaks the build instead
    /// of silently stopping the invariant from being tested.
    /// </summary>
    private sealed class Exposed : CompanioNationSignalRClient
    {
        private Exposed(IJSRuntime js, NavigationManager nav, IConfiguration cfg)
            : base(js, nav, cfg)
        {
        }

        public static bool IsVersionSkewReport(string? message, Exception? ex, string? additionalInfo, bool versionMismatch)
            => CompanioNationSignalRClient.IsVersionSkewReport(message, ex, additionalInfo, versionMismatch);
    }
}
