using System.Text;
using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class ExceptionFormatterTests
{
    [Fact]
    public void WhenAppendChainCalledThenEveryInnerExceptionLevelIsIncluded()
    {
        var sb = new StringBuilder();
        var root = new InvalidOperationException(
            "outer message",
            new ApplicationException(
                "middle message",
                new NotSupportedException("root cause")));

        ExceptionFormatter.AppendChain(sb, root);

        string text = sb.ToString();
        Assert.Contains("outer message", text);
        Assert.Contains("middle message", text);
        Assert.Contains("root cause", text);
        Assert.Contains("ExceptionType: System.InvalidOperationException", text);
        Assert.Contains("-- Inner Exception (1) --", text);
        Assert.Contains("-- Inner Exception (2) --", text);
    }

    [Fact]
    public void WhenAppendChainCalledWithSingleExceptionThenNoInnerMarkerIsAdded()
    {
        var sb = new StringBuilder();
        var ex = new TimeoutException("just this one");

        ExceptionFormatter.AppendChain(sb, ex);

        string text = sb.ToString();
        Assert.Contains("ExceptionType: System.TimeoutException", text);
        Assert.DoesNotContain("Inner Exception", text);
    }

    [Fact]
    public void WhenAppendChainCalledThenHResultIsRenderedWithHex()
    {
        var sb = new StringBuilder();
        var ex = new InvalidOperationException("boom");

        ExceptionFormatter.AppendChain(sb, ex);

        Assert.Contains($"HResult: {ex.HResult} (0x{ex.HResult:X8})", sb.ToString());
    }
}
