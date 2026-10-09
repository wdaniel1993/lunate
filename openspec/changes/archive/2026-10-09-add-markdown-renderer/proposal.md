# Proposal: Markdown renderer and technical formatting (T-19)

## Why

T-18 built the interactive foundation; T-19 starts the output side. The guide's screen model commits finished output to scrollback as Spectre renderables, and the assistant's answers arrive as Markdown. Spectre has no Markdown widget, so we render it ourselves — but parsing is not our business. Real agent output is full of `snake_case`, globs and nested emphasis; CommonMark's inline rules are exactly where a hand-rolled subset parser corrupts the most-read surface. Maintainer review (2026-10-09) settled it: **parse with Markdig, render with our own subset renderer to Spectre**.

## What changes

- **Packages (maintainer-approved):** `Spectre.Console` 0.57.2 + `Markdig` 1.4.0 (BSD-2-Clause) in `Lunate.Tui`; `Spectre.Console.Testing` 0.57.2 in tests. Markdig has no dependencies on net10.0. No Markdown-rendering library, no TextMate, no snapshot framework.
- **`MarkdownRenderer`** (public `Lunate.Tui`): `Render(string) -> IRenderable`. Walks Markdig's AST and maps the guide's subset — headings 1-6, paragraphs, bullet/ordered lists (nested), quotes, fenced code with language label, inline bold/italic/code — to Spectre renderables; code blocks stay borderless. Every user string goes through `Markup.Escape`.
- **Unsupported constructs render as readable plain text** — tables, links, images, HTML, task lists — never raw markup, never throwing (pinned per construct).
- **Keyword highlighting (v1): own small deterministic scanners** for C#, JSON, shell — no TextMate, no highlighting engine.
- **`TechnicalText`** formatting helpers: bytes, token counts, durations, percents — always `CultureInfo.InvariantCulture`, de-AT as the falsifier.
- **Snapshots per feature** via `Spectre.Console.Testing` `TestConsole` → committed goldens, including the edge set: nested lists, unclosed fence, emphasis edge cases (`snake_case_words`, `2*3*4`), escaping, unsupported constructs.

## Done when

A golden exists per Markdown feature and per edge case; escaping tests prove bracket text stays literal; unsupported constructs render plain (no markup, no throw) with a falsifier per construct; `TechnicalText` has table tests incl. de-AT; the licence table records BSD-2-Clause for Markdig; first-render cost measured and reported against the startup budget, which stays untouched while the exe does not load Tui; `scripts/verify.sh` green. Wiring into scrollback/live area stays out of scope (T-20/T-22), as does diff rendering (T-20).
