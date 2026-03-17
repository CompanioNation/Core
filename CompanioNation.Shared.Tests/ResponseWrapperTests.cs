using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class ResponseWrapperTests
{
    [Fact]
    public void WhenSuccessCalledThenIsSuccessIsTrue()
    {
        var result = ResponseWrapper<string>.Success("test data");

        Assert.True(result.IsSuccess);
        Assert.Equal("test data", result.Data);
        Assert.Equal(0, result.ErrorCode);
    }

    [Fact]
    public void WhenSuccessCalledWithMessageThenMessageIsSet()
    {
        var result = ResponseWrapper<int>.Success(42, "custom message");

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Data);
        Assert.Equal("custom message", result.Message);
        Assert.Equal(0, result.ErrorCode);
    }

    [Fact]
    public void WhenSuccessCalledWithoutMessageThenMessageIsEmpty()
    {
        var result = ResponseWrapper<bool>.Success(true);

        Assert.Equal("", result.Message);
    }

    [Fact]
    public void WhenFailCalledThenIsSuccessIsFalse()
    {
        var result = ResponseWrapper<string>.Fail(50000, "Something went wrong");

        Assert.False(result.IsSuccess);
        Assert.Equal(50000, result.ErrorCode);
        Assert.Equal("Something went wrong", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void WhenFailCalledWithAuthErrorCodeThenErrorCodeIsPreserved()
    {
        var result = ResponseWrapper<object>.Fail(ErrorCodes.InvalidCredentials, "Bad credentials");

        Assert.False(result.IsSuccess);
        Assert.Equal(100000, result.ErrorCode);
    }

    [Fact]
    public void WhenSuccessCalledThenVersionIsPopulated()
    {
        var result = ResponseWrapper<string>.Success("data");

        Assert.NotNull(result.Version);
        Assert.NotEqual("", result.Version);
    }

    [Fact]
    public void WhenFailCalledThenVersionIsPopulated()
    {
        var result = ResponseWrapper<string>.Fail(50000, "error");

        Assert.NotNull(result.Version);
        Assert.NotEqual("", result.Version);
    }

    [Theory]
    [InlineData(ErrorCodes.Success, 0)]
    [InlineData(ErrorCodes.UnknownError, 50000)]
    [InlineData(ErrorCodes.InvalidInput, 50001)]
    [InlineData(ErrorCodes.ResourceNotFound, 50002)]
    [InlineData(ErrorCodes.InvalidCredentials, 100000)]
    [InlineData(ErrorCodes.SessionExpired, 100001)]
    [InlineData(ErrorCodes.SubscriptionRequired, 200000)]
    [InlineData(ErrorCodes.SubscriptionExpired, 200001)]
    [InlineData(ErrorCodes.AIServiceUnavailable, 300000)]
    [InlineData(ErrorCodes.AdminUnauthorized, 400000)]
    public void WhenErrorCodeConstantUsedThenHasExpectedValue(int actual, int expected)
    {
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WhenFailCalledWithValueTypeThenDataIsDefault()
    {
        var result = ResponseWrapper<int>.Fail(50000, "error");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Data);
    }

    [Fact]
    public void WhenSuccessCalledWithNullDataThenDataIsNull()
    {
        var result = ResponseWrapper<string>.Success(null!);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data);
    }
}
