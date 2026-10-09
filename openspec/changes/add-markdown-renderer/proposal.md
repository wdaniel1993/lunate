# Proposal: Markdown subset renderer and technical formatting (T-19)

## Why

T-18 built the interactive foundation; T-19 starts the output side. The guide's screen model commits finished output to scrollback as Spectre renderables, and the assistant's answers arrive as Markdown. Spectre has no Markdown widget, so the guide calls for our own subset renderer: headings, emphasis, inline code, lists, quotes, and fenced code with a language label — plus keyword highlighting for C#, JSON and shell. Alongside it, the technical readouts (footer, token counts, timings) need one culture-invariant formatting home before T-20's tool blocks and T-21's footer consume it.

The S-5 spike prototyped both: finished blocks go through `AnsiConsole.Create` with ANSI disabled for the harness, `Markup.Escape` on every user string (the spike's `SpectreBlocks.cs` is the reference), and it pinned Spectre.Console 0.57.2. T-19 productizes: a real subset parser, renderables that survive `TestConsole` snapshot rendering, and the formatting helpers with de-AT coverage.

## What changes

- **Packages (maintainer-gated):** `Spectre.Console` 0.57.2 in `Lunate.Tui`; `Spectre.Console.Testing` 0.57.2 in `Lunate.Tui.Tests` — the S-5 pins, MIT. No Markdown library: own subset renderer, per the guide.
- **`MarkdownRenderer`** (new `rendering` surface in `Lunate.Tui`): own line-based block parser + minimal inline scanner → Spectre `IRenderable`s. Blocks: headings 1-6, paragraphs, bullet lists (indent levels), ordered lists, quotes, fenced code with language label. Inline: bold, italic, inline code. Every user string goes through `Markup.Escape` — bracket text must render literally, never as Spectre markup.
- **Keyword highlighting (v1):** C#, JSON, shell — keywords, strings, comments via small scanners of our own; no highlighting engine, no new packages.
- **`TechnicalText`** formatting helpers: bytes, token counts, durations, percents — always `CultureInfo.InvariantCulture`, with the de-AT suite as the falsifier.
- **Snapshots per Markdown feature:** `TestConsole` renders each feature (and a mixed document) to plain text; goldens committed under `tests/Lunate.Tui.Tests/fixtures/markdown/` with the existing `LUNATE_TUI_UPDATE_GOLDENS=1` regeneration rule.

## Done when

A snapshot exists per Markdown feature (headings, bold, italic, inline code, lists, quotes, fenced code + label, highlighting ×3, mixed document); `TechnicalText` has table tests incl. de-AT; escaping tests prove bracket text stays literal; `scripts/verify.sh` green. Wiring into scrollback and the live area stays out of scope (T-20/T-22), as does diff rendering (T-20).
