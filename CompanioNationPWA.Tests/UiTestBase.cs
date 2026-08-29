using System.Net;
using CompanioNationPWA.Services;
using CompanioNationPWA.Tests.Fakes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace CompanioNationPWA.Tests;

/// <summary>
/// Shared bUnit <see cref="TestContext"/> setup for CompanioNationPWA UI tests.
/// </summary>
public abstract class UiTestBase : IDisposable
{
    protected BunitContext Context { get; }

    protected FakeCompanioNationSignalRClient SignalRClient { get; }

    protected NavigationManager NavigationManager { get; }

    protected UiTestBase()
    {
        Context = new BunitContext();
        Context.JSInterop.Mode = JSRuntimeMode.Loose;

        SignalRClient = new FakeCompanioNationSignalRClient();
        Context.Services.AddSingleton<ICompanioNationSignalRClient>(SignalRClient);
        Context.Services.AddSingleton<CultureService>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SignalR:HubUrl"] = "",
                ["GTM_ID"] = "",
                ["ThirdParty:FacebookSdkEnabled"] = "false",
            })
            .Build();
        Context.Services.AddSingleton<IConfiguration>(configuration);

        var httpClient = new HttpClient(new StubHttpMessageHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        Context.Services.AddSingleton(httpClient);

        Context.Services.AddLocalization();
        Context.Services.AddSingleton<IStringLocalizerFactory, KeyReturningStringLocalizerFactory>();

        NavigationManager = Context.Services.GetRequiredService<NavigationManager>();
    }

    public void Dispose()
    {
        Context.Dispose();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
