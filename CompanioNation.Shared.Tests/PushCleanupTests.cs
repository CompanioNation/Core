using CompanioNationAPI;
using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class PushCleanupTests
{
    [Fact]
    public void WhenRecipientHasPushTokenThenRecipientUserIdIsReturned()
    {
        var result = new SendMessageResult
        {
            PushTokenUserId = 42,
            PushToken = "recipient-token",
            LoginToken = "sender-login-token"
        };

        Assert.Equal(42, PushCleanup.GetUserIdToClear(result));
    }

    [Fact]
    public void WhenPushTokenUserIdMissingThenZeroReturned()
    {
        var result = new SendMessageResult
        {
            PushTokenUserId = 0,
            PushToken = "recipient-token"
        };

        Assert.Equal(0, PushCleanup.GetUserIdToClear(result));
    }

    [Fact]
    public void WhenResultIsNullThenZeroReturned()
    {
        Assert.Equal(0, PushCleanup.GetUserIdToClear(null));
    }
}
