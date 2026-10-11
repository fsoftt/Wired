namespace Wired.Presentation.Formatting;

public static class TimeFormatter
{
    public static string Format(TimeSpan? time)
    {
        if (time is not { } value || value < TimeSpan.Zero)
            return "--:--";

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }
}
