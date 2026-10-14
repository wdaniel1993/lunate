using Lunate.Extensibility.Abstractions;

namespace UiShowcaseExtension;

/// <summary>
/// The status widget: the context usage as an eight-cell bar plus the rounded percentage — accent
/// below <see cref="WarningPercent"/>, warning at or above it. An unknown window (0) shows
/// <c>ctx n/a</c> in dim. The text is formatted invariantly, so it is identical under every
/// machine culture. Presentation only: the model and the run never see it.
/// </summary>
public sealed class UiShowcaseUsageWidget : IStatusWidget
{
    /// <summary>The number of bar cells; the filled count rounds from the usage ratio.</summary>
    internal const int BarCells = 8;

    /// <summary>The percentage at which the widget switches from accent to warning.</summary>
    internal const int WarningPercent = 80;

    public IReadOnlyList<TextSegment> Segments(StatusView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.ContextWindow <= 0)
        {
            return [new TextSegment("ctx n/a", TextStyle.Dim)];
        }

        double ratio = Math.Clamp((double)view.TokensUsed / view.ContextWindow, 0, 1);
        long percent = (long)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
        int filled = (int)Math.Round(ratio * BarCells, MidpointRounding.AwayFromZero);
        string bar = new string('▰', filled) + new string('▱', BarCells - filled);
        var text = FormattableString.Invariant($"{bar} {percent}%");

        return
        [
            new TextSegment(text, percent >= WarningPercent ? TextStyle.Warning : TextStyle.Accent),
        ];
    }
}
