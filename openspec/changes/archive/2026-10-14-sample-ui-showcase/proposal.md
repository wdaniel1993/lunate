# Sample: ui-showcase (T-50)

## Why

T-50 (guide row 638): *"Renderer + status widget; degrades cleanly in print/json/ACP"* — the fitness suite's ui-showcase proof (`docs/spec/extensibility.md` row 120). T-40 built the primitives (`IToolResultRenderer`, `IStatusWidget`, the JSON-serializable `TextSegment` DTOs, the failure policy, the per-mode policies) and pinned them in the spec. What is missing is the end-to-end proof with a real extension: today the TUI renderer/widget tests drive **injected registries** with fixed doubles, and no test runs a real extension's renderer through the loader, or shows the three non-interactive modes staying clean with a renderer and widget loaded. The sample is that proof.

Drafting check (the fitness-suite rule — a sample that needs a workaround means a missing primitive): the required surfaces all exist (`RegisterToolResultRenderer`/`RegisterStatusWidget`, `ToolResultView`/`StatusView`/`TextSegment`, ordinal tool-name matching, empty-list semantics, last-registration-wins with a logged conflict, throw → omit + log + retry). **No new primitives anticipated**; if the implementation surfaces a gap, it stops and reports instead of working around it.

## What Changes

- **Sample `samples/extensions/ui-showcase/`** (abstractions-only): a read-only tool `uikit__bars` (values → compact JSON result text), a renderer that turns the result into styled bar segments (accent bars, dim values; empty segments for unparsable text), and a status widget that renders a usage bar from `StatusView` (accent under 80%, warning at/above, `n/a` when the window is unknown). README with the capability table.
- **Sample tests** (testing kit, real loader): registration present; renderer output for a good result; renderer empty for garbage; widget segments at low/high/unknown usage; the produced segments round-trip through `System.Text.Json`.
- **Mode proofs** (`tests/Lunate.Coding.Tests`, the sample installed into a temp store from the build output — the TestExtensions pattern, new ProjectReference + copy target):
  - **TUI**: a real turn calling `uikit__bars` renders the bars into the scrollback and the widget segment into the footer — the first renderer path through store → loader → registry → session.
  - **print**: stdout is the raw tool result text (no bar glyphs, no widget), exit 0.
  - **JSON**: the JSONL stream stays pure — no UI artifacts; the tool-result event carries the raw text.
  - **ACP**: no UI artifacts on stdout; the session runs.
- **Wiring**: solution entries; the gate's layering edges gain the sample (Abstractions-only, like its siblings).

## Not in Scope

- New contract primitives (none anticipated — stop-and-report if one surfaces).
- Golden changes; new packages; new ADR (implements ADR-0017 and existing spec text).
- `--no-extensions` mode coverage beyond what the existing mode tests already pin.

## Impact

- New `samples/extensions/ui-showcase/` (extension + tests) in the solution.
- `tests/Lunate.Coding.Tests`: ProjectReference + copy target for the sample, one new test file; layering-edge line.
- No API changes, no golden changes, no new packages.
