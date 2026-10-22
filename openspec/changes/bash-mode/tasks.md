# Tasks

## 1. Mode and prompt

- [x] 1.1 Red tests: `!` as the first character of an empty input enters the mode; `!` anywhere else stays literal; `!!` enters it with the silent flag; Esc leaves the mode and restores the text; Enter on `!` alone leaves without running.
- [x] 1.2 Render the prompt as `! ` in the accent colour while the mode is active, at every width band, without changing any fixture that does not use it.
- [x] 1.3 Route the mode through the existing submission path so Enter has one meaning per state; add the scripted key test for the mode's Enter and Esc.
- [x] 1.4 Commit: `feat(tui): enter bash mode from an exclamation prefix`.

## 2. Execution

- [x] 2.1 Red tests: a `!` line runs through `BashTool` with a `ToolContext` carrying the session's workspace and cancellation token; non-zero exit and duration reach the block header; a cancelled command reports cancellation, not a partial run.
- [x] 2.2 Invoke the tool from the submission path with a synthetic call id; commit one tool block through the existing event/renderer path.
- [x] 2.3 Keep the approval policy authoritative: an ask policy prompts before the command runs, a deny policy refuses it, and no path executes before the policy answers.
- [x] 2.4 Commit: `feat(tui): run bash-mode lines through the bash tool`.

## 3. The silent form and the conversation boundary

- [x] 3.1 Red tests: `!` adds the command and its result to the conversation the next request carries; `!!` adds nothing and leaves the message list byte-identical.
- [x] 3.2 Split the two forms at the conversation boundary only — the scrollback block, header and notice are identical.
- [x] 3.3 Refuse a bash submission while a turn is running with a notice naming the run; no queueing, no steering.
- [x] 3.4 Commit: `feat(tui): keep the silent bash form out of the conversation`.

## 4. History

- [x] 4.1 Red tests: a bash line is recalled by Up in bash mode and not in prompt mode; a prompt is not recalled in bash mode; the existing prompt history file format is unchanged.
- [x] 4.2 Add the separate in-memory bash history ring and route Up per mode.
- [x] 4.3 Commit: `feat(tui): give bash mode its own history`.

## 5. Verification and documentation

- [ ] 5.1 Composition test: the real host types `!echo hi`, submits, and commits the block with exit 0 and a duration, and the model receives no request.
- [ ] 5.2 Snapshot goldens for the `!` prompt at the width bands and colour modes; assert that untouched fixtures are byte-identical.
- [ ] 5.3 Update `docs/guide.md` (the mode, the two forms, approval parity, history) and add the VHS tape for the user-visible flow.
- [ ] 5.4 Fast gate: `dotnet tool restore` then `dotnet csharpier check .`, `openspec validate bash-mode --strict`, build, and the Tui/Coding suites; then the full `scripts/verify.sh` on the final head.
- [ ] 5.5 Commit: `docs(tui): document bash mode`.
