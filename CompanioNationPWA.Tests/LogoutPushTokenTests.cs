using System.Reflection;
using CompanioNation.Shared;
using CompanioNationPWA;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CompanioNationPWA.Tests;

/// <summary>
/// Covers the logout push-token clear: <see cref="CompanioNationSignalRClient.Logout"/> must
/// clear the SERVER-side push token using the login token that was valid before the session
/// was torn down. The original bug nulled the in-memory token first, so the clear request
/// carried a null token and was silently ignored — the device kept receiving notifications
/// after logout.
/// </summary>
public class LogoutPushTokenTests
{
    private const string Token = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task WhenLoggingOutThenClearRequestCarriesThePreLogoutLoginToken()
    {
        using var ctx = new BunitContext();
        var client = new TestableClient(
            new StubJsRuntime { LoginGuid = Token },
            ctx.Services.GetRequiredService<NavigationManager>(),
            NewConfig());

        await client.Logout();

        var send = Assert.Single(client.PushSends);
        Assert.Equal(Token, send.LoginToken);   // captured, NOT nulled before the clear
        Assert.Equal(string.Empty, send.PushToken); // the clear itself
    }

    [Fact]
    public async Task WhenNoTokenAvailableThenLogoutStillClearsWithoutThrowing()
    {
        using var ctx = new BunitContext();
        var client = new TestableClient(
            new StubJsRuntime { LoginGuid = null },
            ctx.Services.GetRequiredService<NavigationManager>(),
            NewConfig());

        await client.Logout();

        var send = Assert.Single(client.PushSends);
        Assert.Null(send.LoginToken);
        Assert.Equal(string.Empty, send.PushToken);
    }

    [Fact]
    public async Task WhenLoggingOutThenCachedUserIsCleared()
    {
        using var ctx = new BunitContext();
        var client = new TestableClient(
            new StubJsRuntime { LoginGuid = Token },
            ctx.Services.GetRequiredService<NavigationManager>(),
            NewConfig());
        SetCurrentUser(client, new UserDetails { UserId = 42 });

        await client.Logout();

        Assert.Null(client.CurrentUser);
    }

    // _currentUser is private with no setter; seed it via reflection rather than widening the
    // production API just for a test.
    private static readonly FieldInfo CurrentUserField =
        typeof(CompanioNationSignalRClient).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not find _currentUser field.");

    private static void SetCurrentUser(CompanioNationSignalRClient client, UserDetails user)
        => CurrentUserField.SetValue(client, user);

    private static IConfiguration NewConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SignalR:HubUrl"] = "" })
            .Build();

    /// <summary>
    /// Substitutes the two seam members so a test can drive <see cref="Logout"/> without a live
    /// SignalR connection: <see cref="Initialize"/> is a no-op (no negotiation) and the push
    /// send is captured instead of hitting the hub.
    /// </summary>
    private sealed class TestableClient : CompanioNationSignalRClient
    {
        public TestableClient(IJSRuntime js, NavigationManager nav, IConfiguration cfg)
            : base(js, nav, cfg)
        {
        }

        public List<(string? LoginToken, string PushToken)> PushSends { get; } = [];

        public override Task Initialize() => Task.CompletedTask;

        protected override Task<ResponseWrapper<bool>> UpdatePushTokenOnServerAsync(string? loginToken, string pushToken)
        {
            PushSends.Add((loginToken, pushToken));
            return Task.FromResult(ResponseWrapper<bool>.Success(true));
        }
    }

    /// <summary>Minimal <see cref="IJSRuntime"/> that answers localStorage reads and no-ops writes.</summary>
    private sealed class StubJsRuntime : IJSRuntime
    {
        public string? LoginGuid { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? result = null;

            if (identifier == "localStorage.getItem")
            {
                string key = args is { Length: > 0 } ? args[0]?.ToString() ?? string.Empty : string.Empty;
                if (key == "loginGuid") result = LoginGuid;
            }

            return result is null
                ? ValueTask.FromResult(default(TValue)!)
                : ValueTask.FromResult((TValue)result);
        }
    }
}
