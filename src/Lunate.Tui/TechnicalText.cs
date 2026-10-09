using System.Globalization;

namespace Lunate.Tui;

public static class TechnicalText
{
    private const long Kilobyte = 1024;
    private const long Megabyte = Kilobyte * 1024;
    private const long Gigabyte = Megabyte * 1024;
    private const long Terabyte = Gigabyte * 1024;

    public static string Bytes(long bytes)
    {
        (double value, string unit) = bytes switch
        {
            < Kilobyte => (bytes, "B"),
            < Megabyte => (bytes / (double)Kilobyte, "KB"),
            < Gigabyte => (bytes / (double)Megabyte, "MB"),
            < Terabyte => (bytes / (double)Gigabyte, "GB"),
            _ => (bytes / (double)Terabyte, "TB"),
        };

        string number =
            unit == "B"
                ? bytes.ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.#", CultureInfo.InvariantCulture);
        return $"{number} {unit}";
    }

    public static string Tokens(long tokens) => tokens.ToString("N0", CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan duration)
    {
        double totalSeconds = duration.TotalSeconds;
        if (totalSeconds < 60)
        {
            double seconds = Math.Floor(totalSeconds * 10) / 10;
            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }

        long wholeSeconds = (long)Math.Floor(totalSeconds);
        if (wholeSeconds < 3600)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{wholeSeconds / 60} m {wholeSeconds % 60:00} s"
            );
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{wholeSeconds / 3600} h {wholeSeconds % 3600 / 60:00} m"
        );
    }

    public static string Percent(double percent) =>
        percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
