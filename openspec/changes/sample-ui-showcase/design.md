# Design — sample-ui-showcase

No open questions: this change adds a sample and its proofs, no contract surface. Decisions are recorded; the one structural choice (Coding.Tests referencing the sample) is called out in D4.

## D1 — The sample

`samples/extensions/ui-showcase/`: `UiShowcaseExtension` + `UiShowcaseExtension.Tests`. Abstractions-only (ADR-0017), manifest `ui-showcase`, settings file pattern like the other samples (none needed — no settings).

- **Tool `uikit__bars`** (read-only; `values`: array of integers, required): returns a compact JSON result text `{"values":[3,7,1,9]}` (the renderer's input; the model sees exactly this).
- **Renderer** (`IToolResultRenderer`, `ToolName = "uikit__bars"`): parses the result text; one segment line per value — `█` repeated `value` times in `Accent`, then the numeric value in `Dim` (e.g. `███████ 7`). Unparsable or empty input returns an empty list (shows nothing — the contract's degradation path). Bars are capped (max 40 glyphs) so a hostile value cannot flood the view.
- **Status widget** (`IStatusWidget`): a usage bar from `StatusView` — `▰▰▱▱▱▱▱▱ 25%` (`Accent` under 80%, `Warning` at/above 80%); `ctx n/a` in `Dim` when `ContextWindow` is 0 (unknown); rounds with `CultureInfo.InvariantCulture`.

## D2 — Sample tests (testing kit)

Load the built sample through the real loader in a temp store (the kit pattern) and assert:

1. the renderer and widget are registered (and the tool present);
2. `Render` for `{"values":[3,7,1,9]}` yields the expected segment texts + styles;
3. `Render` for garbage text (and for an empty result) yields an empty list;
4. `Segments` at 25%, at 80%+ and with an unknown window (0) yield the expected texts + styles;
5. the sample's produced segments round-trip through `System.Text.Json` (`AIJsonUtilities` options), pinning the DTO discipline at the sample level.

## D3 — Mode proofs (the fitness row's "degrades cleanly in print/json/ACP")

All in `tests/Lunate.Coding.Tests`, one new file (`UiShowcaseSampleTests`), with the sample **installed from the build output into a temp extension store** (the `ModelFixture.Install` pattern):

- **TUI** (`InteractiveSessionHost`): install → run a scripted turn calling `uikit__bars` (scripted model client enqueues the call) → the scrollback contains the rendered bars, the footer contains the widget segment. This is the first renderer/widget path exercised through store → loader → registry → session (the T-40 tests inject registries directly).
- **print** (`PrintMode.RunAsync`, in-process): with the sample loaded and a turn calling the tool — stdout contains the raw result text, contains no `█`/`▰` glyphs and no widget text; exit 0; stderr has no UI lines.
- **JSON** (`JsonMode`): the JSONL stream stays pure — structurally equivalent to a no-extension run of the same script (same event kinds in order, payloads modulo run/session ids) and free of UI artifacts; the tool-result event carries the raw text.
- **ACP** (`AcpMode`, the AcpInteractionTests pattern): the session runs with the sample loaded; stdout carries no UI artifacts.

## D4 — Wiring decision: Coding.Tests references the sample

The mode proofs must load the **sample** (not a fixture double), but mode seams live in `Lunate.Coding` and the sample cannot reference it. Decision: `Lunate.Coding.Tests` gains a ProjectReference to the sample and a `CopyTestExtensions`-style step that copies its build output to `$(OutDir)Samples/UiShowcaseExtension/`; tests install from there into a temp store. This is the existing TestExtensions pattern applied to a sample — test-only, no product impact, recorded here as the deliberate choice (the alternative, duplicating the sample as a TestExtensions fixture, would drift).

## D5 — Conventions

- README with the capability table (fitness-suite convention, permission-gate shape).
- Sample test project: `[assembly: Xunit.v3.Parallelization(Mode = ParallelMode.None)]` (process-spawning suites serialize — here cheap insurance, consistent with siblings).
- Solution entries for both projects; the gate's layering edges gain the sample (Abstractions-only).

## D6 — Errors and limits

Renderer garbage → empty segments (never a throw; and a throw would be omitted + logged + retried per the contract). Widget unknown window → `ctx n/a`. Bar glyph cap 40. No settings; no background services; no network.
