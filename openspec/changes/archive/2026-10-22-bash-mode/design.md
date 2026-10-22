## Context

The input surface has one submission path (`InteractiveSession.SubmitPromptAsync`) and one shell path (`BashTool`, via the executor that resolves the shell, bounds output, kills the process tree and reports the exit code). Bash mode is a UI mode over those two, not a third path.

## Decisions

**Bash mode is input state, not session state.** The mode lives with the input line (a mode flag plus the buffer), so it cannot outlive the prompt, cannot appear in a session file and needs no migration. Leaving the mode restores the buffer with the `!` prefix intact, so Esc never loses what was typed.

**Entry is a prefix, not a key binding.** `!` is typed like any other character and the mode is entered when it is the first character of an empty input. This keeps the key table unchanged (no new chord, nothing to clash with `Termux` Alt limits) and matches tau and pi, where the prefix is the affordance. `!!` is the same rule with the silent flag set. A `!` whose cursor is not at the start of an empty line is literal, so technical prose about shell operators still arrives as a prompt.

**Execution goes through `BashTool`, not a second runner.** The mode builds the same `ToolContext` the harness builds (workspace root, session identity, cancellation token) and invokes the tool directly with a synthetic call id. Everything the spec pins about the tool — shell resolution, UTF-8, bounded streams, timeout default, process-tree kill, `IsError` on non-zero exit — therefore holds by construction, and a future change to the tool cannot leave the mode behind. A separate `Process.Start` in the input path was rejected: it would need its own approval call, its own bounds and its own kill logic, and would drift.

**Approval is unchanged.** The bash tool is `Execute` risk, so the existing policy decides: a configured ask prompt appears before anything runs, and a deny rule denies the mode exactly as it denies the model. The user typing the command is not a reason to skip the gate — the card for this feature asks for the same approval policy, and a prompt prefix is not a trust decision.

**The silent form never reaches the model.** `!` commits the tool block and adds the exchange to the conversation so the next turn knows what ran; `!!` commits the same block to the scrollback and adds nothing. The model's message list is the boundary: the block is presentation, the conversation entry is context.

**A running turn refuses the mode.** While a turn is active the submission is refused with a notice naming the run; it is neither queued nor steered, because a shell command inserted into a streaming turn has no defined place in the tool-call order.

**History is separate.** Bash lines go to their own ring: Up in bash mode recalls commands, Up in prompt mode recalls prompts, and neither stream is polluted by the other. The existing history file stays prompt-only; bash history is in-memory for the session (a deliberate limit — persisting it would be a second file format to migrate).

## Risks

- **Prompt-width goldens.** Every width band that renders the prompt gains a variant. The snapshot matrix covers a `!` prompt at the existing bands; fixtures that do not touch bash mode must not change.
- **Approval in print mode.** Bash mode is interactive-only; `-p` has no input line, so print mode is unaffected and the spec says so.
- **Composition test or it did not happen.** The acceptance test drives the real host: type `!echo hi`, submit, and assert the block and the conversation entry (or their absence for `!!`).

## Public surface

No change to the session schema, the event schema or the extension abstractions. `/extensions` sees the bash tool call exactly as it sees a model-issued one.
