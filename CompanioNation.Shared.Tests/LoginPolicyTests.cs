using CompanioNationAPI;
using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class LoginPolicyTests
{
    [Fact]
    public void WhenFailedLoginsBelowThresholdThenNotLockedOut()
    {
        Assert.False(LoginPolicy.IsLockedOut(LoginPolicy.MaxFailedLogins - 1));
    }

    [Fact]
    public void WhenFailedLoginsAtThresholdThenLockedOut()
    {
        Assert.True(LoginPolicy.IsLockedOut(LoginPolicy.MaxFailedLogins));
    }

    [Fact]
    public void WhenOAuthAccountExistsThenOAuthCanTakeOverEvenUnverified()
    {
        Assert.True(LoginPolicy.OAuthCanTakeOver(isOAuthAccount: true, emailVerified: false));
    }

    [Fact]
    public void WhenPasswordAccountAndUnverifiedEmailThenOAuthCannotTakeOver()
    {
        Assert.False(LoginPolicy.OAuthCanTakeOver(isOAuthAccount: false, emailVerified: false));
    }

    [Fact]
    public void WhenPasswordAccountAndVerifiedEmailThenOAuthCanTakeOver()
    {
        Assert.True(LoginPolicy.OAuthCanTakeOver(isOAuthAccount: false, emailVerified: true));
    }

    [Fact]
    public void WhenOAuthEmailUnverifiedErrorCodeIsDefined()
    {
        Assert.Equal(100006, ErrorCodes.OAuthEmailUnverified);
    }
}
