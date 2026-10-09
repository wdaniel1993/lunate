# Design: Approval prompt, status footer, key bindings (T-21)

## Structure

New files in `src/Lunate.Tui/Interaction/`: `KeyRouter.cs` (public enum + pure route), `CtrlCQuitWindow.cs` (public), `ApprovalPrompt.cs` + `ApprovalPromptRenderer.cs` (public), `StatusFooter.cs` + `StatusFooterRenderer.cs` + `GitBranchReader.cs` (public). Goldens under `tests/Lunate.Tui.Tests/fixtures/prompt-footer/`. No new packages (Microsoft.Reactive.Testing already present for virtual time).

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
and "always" persistence are adapter concerns (T-22/T-28).

## Deviations

(Filled during apply; empty at proposal time.)
