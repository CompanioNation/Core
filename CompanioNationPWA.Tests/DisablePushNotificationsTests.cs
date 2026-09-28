using CompanioNation.Shared;
using CompanioNationPWA;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CompanioNationPWA.Tests;

/// <summary>
/// Covers the Settings "turn push off" path: <see cref="CompanioNationSignalRClient.DisablePushNotificationsAsync"/>
/// must clear the SERVER-side push token (using the login token that identifies the user), record the
/// client-side opt-out, and unsubscribe the Web Push subscription so an off→on toggle is a genuine reset.
/// If the server clear fails, nothing is torn down and the method returns false so the UI can report it.
/// </summary>
public class DisablePushNotificationsTests
{
    private const string Token = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task WhenDisablingThenClearRequestCarriesTheLoginTokenAndEmptyPushToken()
    {
        using var ctx = new BunitContext();
        var client = new TestableClient(
            new StubJsRuntime { LoginGuid = Token },
            ctx.Services.GetRequiredService<NavigationManager>(),
            NewConfig());

        bool disabled = await client.DisablePushNotificationsAsync();

        Assert.True(disabled);
        var send = Assert.Single(client.PushSends);
        Assert.Equal(Token, send.LoginToken);        // the user is identified by the current login token
        Assert.Equal(string.Empty, send.PushToken);  // the clear itself
    }

    [Fact]
    public async Task WhenDisablingThenTheOptOutIsRecordedAndTheSubscriptionIsUnsubscribed()
    {
        using var ctx = new BunitContext();
        var js = new StubJsRuntime { LoginGuid = Token };
        var client = new TestableClient(
            js,
            ctx.Services.GetRequiredService<NavigationManager>(),
            NewConfig());

        await client.DisablePushNotificationsAsync();

        Assert.Contains("window.setPushOptOut", js.InvokedIdentifiers);
        // Unsubscribing is what makes the toggle a genuine reset: re-enabling creates a fresh
        // subscription instead of reusing a possibly-broken one.
        Assert.Contains("window.unregisterPush", js.InvokedIdentifiers);
    }

    [Fact]
    public async Task WhenServerClearFailsThenNothingIsTornDownAndItReturnsFalse()
    {
        using var ctx = new BunitContext();
        var js = new StubJsRuntime { LoginGuid = Token };
        var client = new TestableClient(
            js,
            ctx.Services.GetRequiredService<NavigationManager>(),
            NewConfig())
        {
            ClearResult = ResponseWrapper<bool>.Fail(ErrorCodes.UnknownError, "boom")
        };

        bool disabled = await client.DisablePushNotificationsAsync();

        Assert.False(disabled);
        // The opt-out flag must NOT be set and the subscription must NOT be unsubscribed:
        // otherwise the UI would claim "off" while the server still sends.
        Assert.DoesNotContain("window.setPushOptOut", js.InvokedIdentifiers);
        Assert.DoesNotContain("window.unregisterPush", js.InvokedIdentifiers);
    }

    [Fact]
    public async Task WhenOptedOutThenUpdatePushTokenIgnoresTheToken()
    {
        using var ctx = new BunitContext();
        var js = new StubJsRuntime { LoginGuid = Token, IsPushOptedOut = true };
        var client = new TestableClient(
            js,
            ctx.Services.GetRequiredService<NavigationManager>(),
            NewConfig());

        await client.UpdatePushToken("some-token");

        // A late token (e.g. a native iOS FCM refresh) must never silently re-enable delivery
        // after the user has turned push off.
        Assert.Empty(client.PushSends);
    }

    private static IConfiguration NewConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SignalR:HubUrl"] = "" })
            .Build();

    /// <summary>
    /// Substitutes the connection seam so a test can drive <see cref="DisablePushNotificationsAsync"/>
    /// without a live hub: <see cref="Initialize"/> is a no-op and the push send is captured.
    /// </summary>
    private sealed class TestableClient : CompanioNationSignalRClient
    {
        public TestableClient(IJSRuntime js, NavigationManager nav, IConfiguration cfg)
            : base(js, nav, cfg)
        {
        }

        public List<(string? LoginToken, string PushToken)> PushSends { get; } = [];

        public ResponseWrapper<bool> ClearResult { get; init; } = ResponseWrapper<bool>.Success(true);

        public override Task Initialize() => Task.CompletedTask;

        protected override Task<ResponseWrapper<bool>> UpdatePushTokenOnServerAsync(string? loginToken, string pushToken)
        {
            PushSends.Add((loginToken, pushToken));
            return Task.FromResult(ClearResult);
        }
    }

    /// <summary>Minimal <see cref="IJSRuntime"/> that answers localStorage reads and records the calls made.</summary>
    private sealed class StubJsRuntime : IJSRuntime
    {
        public string? LoginGuid { get; init; }
        public bool IsPushOptedOut { get; init; }

        public List<string> InvokedIdentifiers { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            InvokedIdentifiers.Add(identifier);

            object? result = null;

            if (identifier == "localStorage.getItem")
            {
                string key = args is { Length: > 0 } ? args[0]?.ToString() ?? string.Empty : string.Empty;
                if (key == "loginGuid") result = LoginGuid;
            }
            else if (identifier == "window.isPushOptedOut")
            {
                result = IsPushOptedOut;
            }

            return result is null
                ? ValueTask.FromResult(default(TValue)!)
                : ValueTask.FromResult((TValue)result);
        }
    }
}
