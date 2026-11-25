using System.Reflection;

namespace CompanioNationPWA.Tests;

public class FacebookDiagnosticsTests
{
    [Theory]
    [InlineData("https://example.com/auth/facebook/callback?code=synthetic-secret&state=synthetic-state#synthetic-fragment", "https://example.com/auth/facebook/callback")]
    [InlineData("https://synthetic-user:synthetic-secret@example.com/auth/facebook/callback", "https://example.com/auth/facebook/callback")]
    [InlineData("not-a-url?code=synthetic-secret", "unknown")]
    public void NavigationDiagnosticsNeverIncludeCredentials(string url, string expected)
    {
        var method = typeof(CompanioNationSignalRClient).GetMethod("SafeNavigationUri", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, method.Invoke(null, new object[] { url }));
    }
}
