## 1. Spec

- [ ] 1.1 Delta spec for the `tui` Slash commands requirement is in place (a `/quit` mid-turn scenario added to the MODIFIED block) and `openspec validate quit-during-active-turn --type change --strict` passes.

## 2. Test (red)

- [ ] 2.1 Replace the pinned mid-turn `/quit` refusal test in `FollowUpQueueSessionTests` with the behaviour test: `/quit` during a gated running turn ends the session loop without releasing the gate, no refusal notice, exit code 0 (already run red — the pump never returns).

## 3. Implement

- [ ] 3.1 In `InteractiveSession.QueueFollowUpAsync`, when the submitted line parses to `/quit`, take the quit path (cancel the running turn via the session's `Quit()`, skip the pipeline) instead of the refusal notice; every other command keeps the refusal.
- [ ] 3.2 Update the stale comment in `InteractiveSessionCommandTests` that calls quit "idle-only"; confirm the busy-refusal test still refuses `/new`, `/resume`, `/model`, `/auth`, `/compact`, `/setup`.

## 4. Verify

- [ ] 4.1 `dotnet test tests/Lunate.Coding.Tests` (touched classes green; new test green).
- [ ] 4.2 Fast gate: Release build, `dotnet csharpier format .` on changed files then `dotnet csharpier check .`.
- [ ] 4.3 Manual process check: a real (`SIGKILL`-free) mid-turn `/quit` run leaves no surviving lunate/vstest child with a tty: `ps -o pid,pgid,tty,comm`.
- [ ] 4.4 `openspec validate quit-during-active-turn --type change --strict`, then `openspec validate --all --strict` at sync/archive time.
