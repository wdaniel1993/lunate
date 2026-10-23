# Status widgets

A status widget contributes extra segments to the terminal's status footer. It is called on each
status update and appends after the core fields in registration order. It is presentation only:
the model, the session file and the run never see its output.

## The API

```csharp
public interface IStatusWidget
{
    IReadOnlyList<TextSegment> Segments(StatusView view);
}
```

`StatusView` carries `Model`, `TokensUsed`, `ContextWindow`, `WorkingDirectory` and `GitBranch`.
`TextSegment` is a `(string Text, TextStyle Style)` pair. Register the widget with
`context.RegisterStatusWidget(widget)`.

## Constraints

- **Every update.** `Segments` runs on each status update; keep it allocation-light and never block.
- **Empty is valid.** Returning an empty list shows nothing.
- **Failure is contained.** A widget that throws is omitted for that update, logged, and retried on
  the next update. It does not stop other widgets.
- **Presentation only.** Segments never reach the model, the session file or the run. Widgets exist
  only in the interactive TUI; print, JSON and ACP modes ignore them.
- **Registration only.** Do not render in `Create` and do not start resources there.
- **No cap.** Every registered widget contributes, in registration order.

## Example

`ui-showcase` draws the context usage as an eight-cell bar plus the rounded percentage. The text is
formatted invariantly, so the same machine culture produces the same output.

```csharp
public sealed class UiShowcaseUsageWidget : IStatusWidget
{
    internal const int BarCells = 8;
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
```

Full file:
[`UiShowcaseUsageWidget.cs`](../../../samples/extensions/ui-showcase/UiShowcaseExtension/UiShowcaseUsageWidget.cs).
The factory registers it with `context.RegisterStatusWidget(new UiShowcaseUsageWidget());`.

## See also

- UI capabilities by mode: [`docs/spec/extensibility.md`](../../spec/extensibility.md#ui-by-mode)
- Tool-result renderers: [tool-result-renderers.md](tool-result-renderers.md)
- Culture-invariant formatting: [`AGENTS.md`](../../../AGENTS.md)
