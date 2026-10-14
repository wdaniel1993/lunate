# Sample: ui-showcase

A UI-contribution extension: it registers the read-only `bars` tool (model-facing `uikit__bars`),
an `IToolResultRenderer` that draws the tool's result as accent bar lines above the live area, and
an `IStatusWidget` that draws the context usage as a bar in the status footer. The sample proves
the T-40 UI abstractions through the real pipeline — store → loader → registries → session — and
that print, JSON and ACP modes degrade cleanly when both contributions are loaded.

## Core capabilities proven

| Capability | Where |
| --- | --- |
| Renderer through the real loader | `UiShowcaseBarsRenderer` is registered by the factory, matched by the model-facing tool name `uikit__bars` (ordinal), and its segments reach the scrollback in the interactive session — the first renderer path exercised through the extension store and loader, not an injected registry. |
| Status widget | `UiShowcaseUsageWidget` appends one segment to the footer on every status update: `▰▰▱▱▱▱▱▱ 25%` (accent below 80%, warning at or above; `ctx n/a` in dim when the context window is unknown). |
| Empty-list degradation | Unparsable, empty or non-integer result text renders nothing (the renderer returns an empty list); a throwing renderer would be omitted, logged and retried by the registry. |
| Glyph cap | A value is drawn with at most 40 `█` glyphs (the true number stays visible), so a hostile value cannot flood the view. |
| Mode degradation | **TUI**: bars render; the footer carries the widget. **print**: stdout stays the raw text — no glyphs, no widget. **JSON**: the JSONL stream is structurally identical to a run without the extension — no UI artifacts anywhere. **ACP**: nothing new on stdout; the session runs. |
| Testing kit | `UiShowcaseExtension.Tests` installs the built extension through the real loader in a temp store and asserts registration, renderer output, widget output and the `System.Text.Json` round-trip of the produced segments; `tests/Lunate.Coding.Tests` proves the four modes with the sample installed into a temp store. |

## Layout

```text
samples/extensions/ui-showcase/
  UiShowcaseExtension/        the extension: manifest, factory, tool handler, renderer, widget
  UiShowcaseExtension.Tests/  kit-based tests over the real loader
  README.md
```

## Semantics

- **Tool** — `bars` is read-only and takes `values` (required, an array of integers). The result
  text is the compact JSON echo `{"values":[3,7,1,9]}` — exactly what the model sees; an invalid
  argument is a teaching error naming the fix.
- **Renderer** — one line per value: `█` repeated `value` times in `Accent`, then the numeric value
  in `Dim` (`███████ 7`). Garbage and empty results return an empty list.
- **Widget** — the usage ratio (`TokensUsed / ContextWindow`) rounds to the percentage and to the
  filled cells of an eight-cell bar. Below 80% the segment is `Accent`, at or above it `Warning`;
  a window of 0 shows `ctx n/a` in `Dim`. The text is formatted with
  `CultureInfo.InvariantCulture`.
- **Identity** — the sample's manifest id is `uikit`, so its tool's model-facing name is
  `uikit__bars` (the extension-tool naming grammar: `<id>__<local name>`); the renderer declares
  that exact name.

## Honest limits

- **Presentation only.** Segments never reach the model, the session file or the run state; the
  JSON/print proofs pin that the modes are structurally unchanged with the sample loaded.
- **The renderer is deliberately forgiving.** It recognises only the shape its own tool produces;
  any other result text (including another extension's) renders nothing rather than guessing.
- **The widget is a demo shape, not a cost model.** The percentage is tokens over the declared
  context window; no budget or cost logic.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `tools`).
2. Keep the manifest `tools` list in sync with the registered local names — the loader refuses a
   registered-but-undeclared tool.
3. Declare `IToolResultRenderer.ToolName` with the model-facing name (`<id>__<local name>`), not
   the local name.
4. Keep the tests: they install the built extension through the real loader in a temp store, so
   tests never touch the real `~/.lunate`.

See `templates/extension/README.md` for the full manifest and registration reference.
