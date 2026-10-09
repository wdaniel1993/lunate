# Proposal: Approval prompt, status footer, key bindings (T-21)

## Why

The TUI has renderers and an input line but no interaction rules: which key does what, where approval happens, and what the session continuously shows. The guide fixes the contract ("Key bindings (one meaning each)", `ApprovalPrompt`: "yes, no, always for this session", `StatusFooter`: "model, tokens, context used in percent, working directory, git branch") and this change turns it into components with scripted key tests for every binding — the card's done-criterion — all on virtual time, no wall clock.

## What changes

- **`KeyRouter`**: pure classification of decoded `KeyEvent`s into routed intents — `Submit` (Enter), `Cancel` (Esc), `ClearOrQuit` (Ctrl+C), `ModelPicker` (Ctrl+L), `HistoryPrevious`/`HistoryNext` (Up/Down), `Edit` (everything else, incl. Alt+Enter and Ctrl+J which `InputLine` already turns into newlines). One meaning per key, per the guide's table.
- **`CtrlCQuitWindow`**: Ctrl+C on non-empty input clears it (and disarms); on empty input, a first press arms a hint and a second press within 2 s quits. Runs on the injected `IScheduler` (virtual time in tests); it never cancels a turn.
- **`ApprovalPrompt`**: model + renderable (`Allow <tool> <summary>?  [y]es  [n]o  [a]lways`) + a pure `Decide(KeyEvent) -> ApprovalChoice?` (Approve/Deny/Always). Explicit keys only; a stray Enter is a **deny** — nothing runs by accident. "Always" session memory stays in the T-22 adapter; the prompt only produces the choice.
- **`StatusFooter`**: model (model id, tokens used, context window, cwd, git branch) + renderer — one dim line, culture-invariant token/percent formatting, deterministic degradation when narrow. **`GitBranchReader`**: reads `.git/HEAD` directly (directory or worktree `gitdir:` file), detached → short SHA, any failure → omitted; no process spawn.

## Done when

Every binding in the guide's table has a scripted key test; the Ctrl+C window is proven on virtual time (no wall-clock reads anywhere); approval prompt and footer have goldens incl. the safe-default and degradation cases; no new packages; `scripts/verify.sh` green. Wiring into a running session (steering, cancel semantics, pickers, history storage, Tab completion) stays with T-22.
