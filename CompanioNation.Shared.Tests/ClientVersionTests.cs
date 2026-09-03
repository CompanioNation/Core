using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class ClientVersionTests
{
    [Theory]
    [InlineData("26.9.2.4", "26.9.2.4", false)]
    [InlineData("26.9.2.3", "26.9.2.4", true)]
    [InlineData("26.9.2.10", "26.9.2.4", false)]   // numeric, not string, ordering
    [InlineData("26.9.2.4", "26.9.2.10", true)]
    [InlineData("27.0.0.0", "26.9.9.9", false)]
    [InlineData("26.9", "26.9.2", true)]           // shorter = missing segments treated as 0
    public void WhenIsOlderThanCalledThenComparesNumerically(string actual, string minimum, bool expected)
    {
        Assert.Equal(expected, ClientVersion.IsOlderThan(actual, minimum));
    }

    [Theory]
    [InlineData(null, "26.9.2.4")]
    [InlineData("", "26.9.2.4")]
    [InlineData("   ", "26.9.2.4")]
    [InlineData("not-a-version", "26.9.2.4")]
    public void WhenActualIsMissingOrUnparsableThenFailsClosed(string? actual, string minimum)
    {
        Assert.True(ClientVersion.IsOlderThan(actual, minimum));
    }

    [Fact]
    public void WhenMinimumIsUnparsableThenNeverRequiresUpgrade()
    {
        Assert.False(ClientVersion.IsOlderThan("26.9.2.4", "garbage"));
    }

    [Fact]
    public void EveryRequestDtoInheritsHubRequest()
    {
        var requestTypes = typeof(HubRequest).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.Name.EndsWith("Request", StringComparison.Ordinal)
                        && typeof(HubRequest).IsAssignableFrom(t));

        Assert.Contains(requestTypes, t => t == typeof(HubRequest));

        foreach (var type in requestTypes.Where(t => t != typeof(HubRequest)))
        {
            Assert.True(typeof(HubRequest).IsAssignableFrom(type),
                $"{type.Name} must inherit HubRequest so every hub call carries ClientVersion.");
        }
    }

    [Fact]
    public void ClientUpgradeRequiredIsUniqueAcrossErrorCodes()
    {
        var values = typeof(ErrorCodes).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f => (int)f.GetRawConstantValue()!);

        Assert.Equal(1, values.Count(v => v == ErrorCodes.ClientUpgradeRequired));
    }
}
