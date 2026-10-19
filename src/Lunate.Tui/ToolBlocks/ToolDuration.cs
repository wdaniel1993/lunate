using System.Globalization;

namespace Lunate.Tui;

/// <summary>
/// Formats a tool call's measured duration compactly and culture-invariantly: one decimal below ten
/// seconds (<c>0.4s</c>), whole seconds under a minute (<c>12s</c>), then minutes and seconds
/// (<c>1m 02s</c>). Truncates, never rounds up.
/// </summary>
internal static class ToolDuration
{
    public static string Format(TimeSpan duration)
    {
        double seconds = Math.Max(0, duration.TotalSeconds);
        if (seconds < 10)
        {
            double truncated = Math.Floor(seconds * 10) / 10;
            return truncated.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        long whole = (long)seconds;
        if (whole < 60)
        {
            return whole.ToString(CultureInfo.InvariantCulture) + "s";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}m {whole % 60:00}s");
    }
}
