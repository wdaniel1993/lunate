# Welcome header

## Why

An interactive start today shows only the `loading project…` live line: no identity, no version, no model, no workspace, no hint that `/help` exists. The first screen of a session should say what this is, what it runs on, where it is pointed and where to go next — once, without ceremony, and without ever blocking the composition that is already running underneath.

Design-first card: the design below was signed off by the maintainer on 2026-10-18 (wordmark variant V2, the small crescent lockup, chosen from three rendered variants).

## What changes

- A **welcome header** prints once per interactive session start, on the first frame, as finished Spectre output into scrollback. Not a splash: no animation, no blocking, no alternate screen; the composition (extensions, MCP, skills) keeps running underneath and the `loading project…` line continues as today. The header is included in the startup budget gate.
- Content (top to bottom): crescent wordmark (V2) + version; model id + context window; workspace path (`~`-shortened); `C# workspace · Roslyn` when detected; extension/MCP/skill counts (`…` placeholder until composition finishes, then a single in-place update line — never a redraw of the header block); a hint line naming `/help`.
- **First-run path**: when no model is configured, or no provider/key resolves, the header prints exactly one actionable fix (no model → `set 'model' in ~/.lunate/settings.json`; no key file → `add the '<provider>' key to ~/.lunate/auth.json`; env var present but not resolving → the var is named). No counts/hints block in that state; the workspace line stays.
- **Capability-aware**: full block only at width ≥ 80 with Unicode; otherwise a single-line ASCII form (`lunate v0.1.0 (C# · Roslyn) — /help for commands`). `NO_COLOR` honored; mintty/legacy console (no ANSI) gets the ASCII form without colors. Never in print, ACP or `--json` modes. `startupHeader: false` (settings) and `LUNATE_STARTUP_HEADER=0` (env) turn it off. `/new` shows it again.
- New **`/help`** command: lists commands grouped (session / model / extensions / files / quit) with one-line descriptions, from the command registry — no hand-maintained list; a command without a description fails a test.
- The chosen mark also ships as the **README logo** (light/dark) and the **GitHub social preview** (1280×640, dark, wordmark + one-line pitch).

## Impact

- `src/Lunate.Coding/InteractiveSession.cs` (first frame), `InteractiveSession.Commands.cs` (`/help` + registry), `SettingsStore.cs` (`startupHeader`), a new header renderer (likely `src/Lunate.Tui/`).
- Specs: `tui` (welcome header added; slash-command list gains `/help`), `agent-config` (the setting).
- Tests: snapshot matrix (widths {80, 120, 40} × Unicode/ASCII × color/NO_COLOR), first-run variant, `startupHeader: false`, `/new` reprint, print/ACP/`--json` absence, production composition test (header present, budget green).
- No new packages. Docs: `/help` is the new pointer; README media lands with the user-docs card.
