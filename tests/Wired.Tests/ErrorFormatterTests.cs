using Wired.Presentation.Formatting;

namespace Wired.Tests;

public class ErrorFormatterTests
{
    [Fact]
    public void Describes_the_whole_exception_chain_with_types()
    {
        var error = new InvalidOperationException("outer", new HttpRequestException("403 Forbidden"));

        var text = ErrorFormatter.Describe(error);

        Assert.Equal("InvalidOperationException: outer\n→ HttpRequestException: 403 Forbidden", text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Report_includes_stack_trace_and_is_bounded()
    {
        Exception error;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception thrown)
        {
            error = thrown;
        }

        var report = ErrorFormatter.DescribeForReport(error, maxLength: 200);

        Assert.StartsWith("InvalidOperationException: boom", report);
        Assert.Contains(" at ", report);
        Assert.True(report.Length <= 202);
    }
}
