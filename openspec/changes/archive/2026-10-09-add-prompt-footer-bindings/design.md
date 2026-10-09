# Design: Approval prompt, status footer, key bindings (T-21)

## Structure

New files in `src/Lunate.Tui/Interaction/`: `KeyRouter.cs` (public enum + pure route), `CtrlCQuitWindow.cs` (internal — an `IScheduler` constructor cannot be public under ADR-0007; see Deviations 1), `ApprovalPrompt.cs` + `ApprovalPromptRenderer.cs` (public), `StatusFooter.cs` + `StatusFooterRenderer.cs` + `GitBranchReader.cs` (public). Goldens under `tests/Lunate.Tui.Tests/fixtures/prompt-footer/`. No new packages (Microsoft.Reactive.Testing already present for virtual time).

## Key routing (one meaning each, per the guide's table)

```text
KeyEvent -> RoutedKey
  Enter, no modifiers        -> Submit
  Alt+Enter | Ctrl+J         -> Edit        (InputLine inserts the newline)
  Escape                     -> Cancel
  Ctrl+C                     -> ClearOrQuit (the window decides clear vs quit)
  Ctrl+L                     -> ModelPicker
  Up                         -> HistoryPrevious
  Down                       -> HistoryNext
  Tab                        -> Edit        (completion is T-22; currently a no-op)
  anything else              -> Edit
```

`KeyRouter` is pure and stateless; Ctrl+C's state lives in `CtrlCQuitWindow(IScheduler, window = 2 s)`:
`Press(inputEmpty) -> ClearInput | Arm | Quit | ReArm`. Non-empty input always clears (and disarms);
empty input arms, and a second press within the window quits. Time comes from `scheduler.Now` —
virtual in tests. `IsArmed`/hint text are exposed for the footer notice (T-22 shows it).

## Approval prompt

`ApprovalPromptModel(string ToolName, string ArgsSummary)` — T-22 maps `ApprovalRequested(ToolName, Args)`
and summarizes args with T-20's `ToolArgsSummary`. Renderable: one borderless block,
`Allow <tool> <summary>?  [y]es  [n]o  [a]lways this session`, every string escaped, yellow question
marker. `Decide(KeyEvent) -> ApprovalChoice?` (`Approve`/`Deny`/`Always`): `y`/`n`/`a` bare only;
**Enter → Deny** (a stray Enter never runs a tool — safe default, documented decision); Esc and all
other keys → `null` (the session resolves Esc as cancel, T-22). Session memory for `Always` belongs to
the T-22 adapter, not this component.

## Status footer

`StatusFooterModel(string Model, long TokensUsed, long ContextWindow, string WorkingDirectory, string? GitBranch)`.
One dim line, middle-dot separators: `deepseek-v4.1-flash · 12.4k/128k (10%) · ~/dev/lunate · main`.
Token/percent formatting is culture-invariant (compact k/M, one decimal, integer percent; percent
omitted when the window is unknown/zero). Narrow widths degrade deterministically: drop the branch,
then the directory; model and usage always survive. No git branch and no color when the model says so —
the renderer stays a pure function of the model.

`GitBranchReader.Read(string workingDirectory) -> string?`: `.git` directory → parse `HEAD`
(`ref: refs/heads/<name>` → `<name>`; detached → first 7 chars of the SHA); `.git` file (worktree) →
follow `gitdir:` and read that HEAD; missing/unreadable → `null`. File reads only — no process spawn
(a branch read happens per frame; a git invocation per frame is not acceptable).

## Virtual time

The Ctrl+C window and all scripted key tests run on `Microsoft.Reactive.Testing`'s `TestScheduler`
(the T-18 pattern): no `Thread.Sleep`, no wall-clock reads in `Lunate.Tui` or its tests. The
"scripted key tests for every binding" matrix is one theory over the guide's table (key event →
routed intent → component effect), plus per-component falsifiers.

## Out of scope

Wiring to a running session: steering queue, Esc-cancel semantics, pickers UI, history storage
(`~/.lunate/history`), Tab completion, footer notices for armed-quit — all T-22. Approval risk levels
and "always" persistence are adapter concerns (T-22/T-28). T-22 also reconciles this footer's compact
k/M token format with `LiveAreaRenderer`'s existing internal footer form (raw `tok`/`% ctx`): this
renderer is the status/scrollback variant, that one the live-area variant — one format wins at wiring.

## Deviations

Recorded during apply (T-21):

- **`CtrlCQuitWindow` and `CtrlCAction` are internal, not public.** The constructor takes
  `IScheduler`, and `PublicApiTests.No_public_member_exposes_a_system_reactive_type` plus the
  card's "no Rx types public" rule forbid a public member exposing a `System.Reactive` type.
  The window therefore joins `LiveArea`/`KeyReader` as an internal component; tests and the T-22
  wiring in `lunate` reach it through `InternalsVisibleTo`.
- **Footer token format** pins one decimal in k/M segments (`12.4k`, `128.0k`, `1.0M`) and
  switches to M as soon as the k value rounds to 1000.0 (`999_999 → 1.0M`, `999_949 → 999.9k`),
  per the apply brief; the design example's `128k` above was informal.
- **Percent is the rounded integer** (`12_400/128_000 → 10%`), not truncated; this matches the
  design example.
- **Zero context window** renders `tokens/0` and omits only the percent segment.
- **Decision keys live on `ApprovalPromptModel.Decide`** (instance method, pure) rather than a
  free-standing helper; the renderer calls `ToolArgsSummary` itself so raw JSON works.
- **Shift+Enter** inserts a newline best-effort (`InputLine.Apply`) and routes to `RoutedKey.Edit`,
  never `Submit`; the guide documents Alt+Enter and Ctrl+J because terminals usually cannot
  report Shift+Enter, but the apply brief asked for the fallback.
- **Unreadable-HEAD test** simulates the failure with a directory named `HEAD` (portable across
  runners) instead of Unix file modes.
- **Extreme narrow widths**: the renderer keeps model and usage whole even when the line exceeds
  the requested width; the client clips. The specification only requires that they survive.

## T-22 seams

- `ApprovalRequested(ToolName, Args)` maps to `ApprovalPromptModel` (raw args summarized with
  `ToolArgsSummary`, which the renderer already applies) and the `Decide` result; "always"
  session memory stays in the adapter (T-22/T-28).
- The armed hint (`CtrlCQuitWindow.IsArmed` + `Hint`) is shown by the live-area footer notice in
  T-22; nothing is wired here.
- `KeyRouter.Route` feeds the session loop; `RoutedKey.ClearOrQuit` is resolved by
  `CtrlCQuitWindow.Press(inputEmpty)`, with the input-empty flag T-22 supplies.
