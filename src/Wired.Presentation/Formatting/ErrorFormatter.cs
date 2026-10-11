using System.Text;

namespace Wired.Presentation.Formatting;

public static class ErrorFormatter
{
    /// <summary>
    /// Readable, copyable description of an error: every exception in the chain with its type,
    /// so a user report is enough to diagnose the problem.
    /// </summary>
    public static string Describe(Exception error)
    {
        var text = new StringBuilder();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (text.Length > 0)
                text.AppendLine().Append("→ ");
            text.Append(current.GetType().Name).Append(": ").Append(current.Message);
        }

        return text.ToString();
    }

    /// <summary>
    /// <see cref="Describe"/> plus the stack traces (on Android they include the Java frames),
    /// for bug reports copied by the user.
    /// </summary>
    public static string DescribeForReport(Exception error, int maxLength = 6000)
    {
        var report = $"{Describe(error)}\n\n{error}";
        return report.Length <= maxLength ? report : report[..maxLength] + "\n…";
    }
}
