using CompanioNationAPI;

namespace CompanioNation.Shared.Tests;

/// <summary>
/// Input-guard tests for Database methods whose guards return before any database
/// connection is opened — no SQL involved, so they run anywhere.
/// </summary>
public class DatabaseInputValidationTests
{
    [Fact]
    public async Task WhenEmailIsNullThenCheckEmailExistsReturnsEmptyResult()
    {
        var db = new Database();

        var result = await db.CheckEmailExistsAsync(null!);

        Assert.False(result.emailExists);
        Assert.False(result.oauthRequired);
    }

    [Fact]
    public async Task WhenEmailIsWhitespaceThenCheckEmailExistsReturnsEmptyResult()
    {
        var db = new Database();

        var result = await db.CheckEmailExistsAsync("   ");

        Assert.False(result.emailExists);
        Assert.False(result.oauthRequired);
    }

    [Fact]
    public async Task WhenEmailIsNullThenCreateNewUserReturnsNull()
    {
        var db = new Database();

        string? result = await db.CreateNewUserAsync(null!, "password", "1.2.3.4");

        Assert.Null(result);
    }

    [Fact]
    public async Task WhenPasswordIsNullThenCreateNewUserReturnsNull()
    {
        var db = new Database();

        string? result = await db.CreateNewUserAsync("user@example.com", null!, "1.2.3.4");

        Assert.Null(result);
    }

    [Fact]
    public async Task WhenLoginTokenInvalidThenGuaranteeUserFailsWithoutDatabase()
    {
        var db = new Database();

        var result = await db.GuaranteeUserAsync("not-a-guid", null!, null!);

        Assert.False(result.IsSuccess);
        Assert.Equal(100000, result.ErrorCode);
    }

    [Fact]
    public async Task WhenLoginTokenInvalidThenSetConnectionReviewFailsWithoutDatabase()
    {
        var db = new Database();

        var result = await db.SetConnectionReviewAsync("not-a-guid", 1, 5, "Great person");

        Assert.False(result.IsSuccess);
        Assert.Equal(100000, result.ErrorCode);
    }

    [Fact]
    public async Task WhenLoginTokenInvalidThenSetConnectionReviewVisibilityFailsWithoutDatabase()
    {
        var db = new Database();

        var result = await db.SetConnectionReviewVisibilityAsync("not-a-guid", 1, true);

        Assert.False(result.IsSuccess);
        Assert.Equal(100000, result.ErrorCode);
    }
}
