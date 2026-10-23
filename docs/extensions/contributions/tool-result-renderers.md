# Tool-result renderers

A tool-result renderer draws one tool's result in the terminal, as styled text segments. It is
presentation only: the model, the session file and the run never see its output.

## The API

```csharp
public interface IToolResultRenderer
{
    string ToolName { get; }

    IReadOnlyList<TextSegment> Render(ToolResultView view);
}
```

`ToolResultView` carries `ToolName`, `CallId`, `ResultText` and `IsError`. `TextSegment` is a
`(string Text, TextStyle Style)` pair; `TextStyle` is `Plain`, `Dim`, `Accent`, `Warning` or
`Error`. Register the renderer with `context.RegisterToolResultRenderer(renderer)`.

## Constraints

- **Match the model-facing name.** `ToolName` is matched ordinally against the tool call's name. For
  an extension tool that is `<extension-id>__<local-name>` — `uikit__bars`, not `bars`. A renderer
  may be registered for a tool that is not registered yet, such as an MCP tool in a resumed
  session.
- **Last registration wins.** Several renderers for one `ToolName` resolve to the last one, with a
  warning naming both extensions.
- **Show nothing when unsure.** Returning an empty list shows nothing. A renderer that throws is
  omitted for that result, logged, and retried on the next result.
- **Presentation only.** Segments never reach the model, the session file or the run. Custom
  renderers exist only in the interactive TUI; print, JSON and ACP modes ignore them.
- **Registration only.** Do not render in `Create` and do not start resources there.

## Example

`ui-showcase` matches its own tool by the model-facing name and shows nothing for a result it
cannot parse:

```csharp
public sealed class UiShowcaseBarsRenderer : IToolResultRenderer
{
    public string ToolName => "uikit__bars";

    public IReadOnlyList<TextSegment> Render(ToolResultView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!TryParseValues(view.ResultText, out List<int> values) || values.Count == 0)
        {
            return [];
        }

        // One accent bar and one dim numeric segment per value.
        return [];
    }
}
```

The full renderer parses `{"values":[…]}` and caps both the glyph count per value and the number of
rendered values:
[`UiShowcaseBarsRenderer.cs`](../../../samples/extensions/ui-showcase/UiShowcaseExtension/UiShowcaseBarsRenderer.cs).
Registration, from the factory:

```csharp
context.RegisterToolResultRenderer(new UiShowcaseBarsRenderer());
```

Full file:
[`UiShowcaseExtensionFactory.cs`](../../../samples/extensions/ui-showcase/UiShowcaseExtension/UiShowcaseExtensionFactory.cs).

## See also

- UI capabilities by mode: [`docs/spec/extensibility.md`](../../spec/extensibility.md#ui-by-mode)
- Status widgets: [status-widgets.md](status-widgets.md)
- Tools whose results are rendered: [tools.md](tools.md)
