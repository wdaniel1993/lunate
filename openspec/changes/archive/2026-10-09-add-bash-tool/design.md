# Design: add-bash-tool

## Context

Guide lines 487-499 pin the resolution table, the WSL exclusion, the kill mechanism and the UTF-8 rule; the tool-loop spec pins `ToolOutput.Truncate` (30k, model-facing, downstream). Card T-15 adds "truncation" and the three-runner gate. No new packages, no ADR.

## Shell resolution (binding)

Order (first hit wins), exactly per the guide table:

| Platform | Order | Invocation |
| --- | --- | --- |
| macOS, Linux | `bash` (PATH, else `/bin/bash`), then `/bin/sh` | `bash -c "<command>"` — no login shell (env is inherited; login profiles are slow or print banners) |
| Windows | Git Bash (standard install paths: `%ProgramFiles%\Git\bin\bash.exe`, `%ProgramFiles(x86)%\Git\bin\bash.exe`, `%LOCALAPPDATA%\Programs\Git\bin\bash.exe`; else derive `bin\bash.exe` from `git` on PATH), then `pwsh` 7 (PATH, else `%ProgramFiles%\PowerShell\7\pwsh.exe`), then `powershell.exe`, then `cmd.exe` | Git Bash: `-c`; PowerShell both: `-NoProfile -NonInteractive -Command`; cmd: `/c` |

- **Never** `C:\Windows\System32\bash.exe` (that is WSL — Linux paths). Unit test asserts the exclusion even when that path "exists" in the probe.
- "Models write bash best, so bash wins whenever it exists" (guide).
- `ShellResolver` resolves once and caches. An explicit **override path** parameter exists as the seam T-16's `settings.json` and `/shell` will use; T-15 wires no settings itself.
- Result: `(Kind, ExecutablePath, DisplayName, ArgumentsPrefix)`; `DisplayName` ∈ {`bash`, `/bin/sh`, `Git Bash`, `pwsh 7`, `Windows PowerShell`, `cmd`}. On Windows, a Git Bash result is presented as `Git Bash` so the model writes bash dialect.
- Testability: platform probes (OS id, file-exists, PATH value) are injectable, so every Windows order branch is testable on macOS/Linux CI; the real end-to-end runs happen per-OS on the CI matrix.
- No shell found → the tool returns a clear error result (never half-runs).

## `BashTool` (binding)

- Name `bash`; risk `ToolRisk.Execute` (approval policy applies); no annotations in v1.
- Schema: `{ "command": string, required }` — one string; no working-directory parameter in v1 (commands may `cd` themselves); no `timeout` parameter in v1.
- **Description** names the resolved shell and the fixed rules, e.g.: "Run a command in Git Bash (bash dialect). Runs in the workspace root; non-interactive (stdin is closed); output decodes as UTF-8; the process tree is killed on timeout or cancel." — computed from the resolver at construction (resolution is cheap; cache it).
- Execution: `ProcessStartInfo` with `ArgumentList` = prefix + command (single argument, no quoting traps), `WorkingDirectory = workspace.WorktreeRoot`, stdout/stderr redirected, stdin closed, `UseShellExecute = false`, environment inherited unchanged.
- **Capture**: stdout and stderr read concurrently into separate bounded buffers; each stream capped at **1,000,000 characters** — on hitting the cap the buffer stops and appends a marker line ("... [output capped at 1000000 characters; the rest was discarded]"). Model-facing truncation stays the loop's job (`ToolOutput.Truncate`, 30k) — no double markers.
- **Result text**: stdout, then a labeled `stderr:` section when non-empty (deterministic order; documented limitation vs interleaving), then a footer line: `exit code: N` (culture-invariant) — or `timed out after 120s; process tree killed` / `no shell found` / spawn-failure text.
- **Exit semantics**: `IsError = exit != 0 || timedOut || spawnFailed`; the exit code always appears in the text.
- **Timeout**: default 120 s (ctor-overridable for tests); on expiry → terminate the process tree, children first (POSIX: `ps` enumeration + SIGKILL via `PosixProcessTree`; Windows: `Process.Kill(entireProcessTree: true)` — see Deviations), wait briefly, return the error result. **Cancel**: same kill; rethrow `OperationCanceledException` for the loop's cancellation path.
- **Boundary**: cwd is the workspace root (worktree); no path sandboxing — the approval policy is the guard (documented; `bash` asks by default at the policy level).

## Deviations

- **Tree-kill mechanism on POSIX**: the guide pins `Process.Kill(entireProcessTree: true)`. Its Unix implementation SIGSTOPs the whole tree before SIGKILLing it, and on macOS CI (macos-26-arm64 runners) that call **wedges the VM**: the hosted runner loses communication, no logs or step timeouts fire, and the job dies 20–46 minutes later. A minimal repro on the runner (spawn `sh -c 'exec sleep 300' & sleep 300`, then call the framework tree kill) reproduces the wedge every time, while a `ps`-walk + children-first SIGKILL completes in milliseconds (probe evidence on PR #33). POSIX therefore uses `PosixProcessTree` (enumerate with `ps -axo pid=,ppid=`, SIGKILL children before parents, rate-limited by a visited set); Windows keeps the framework path (job objects — unaffected, and green across CI). The requirement is unchanged: the tree must die on timeout/cancel, still proven by the grandchild-PID tests. The guide line will be reconciled when the runner image issue is confirmed fixed or the card closes.

## Tests

- `ShellResolverTests` (all OSes): every order branch via probes — Windows: Git Bash install paths → git-derived → pwsh 7 → powershell → cmd; **System32 WSL exclusion**; Unix: bash → /bin/sh; override wins; display names; no-shell → null.
- `BashToolTests`: echo round-trip + exit code 0; non-zero exit → `IsError` with code in text; stdout/stderr separation; **UTF-8** round-trip (umlauts + CJK + emoji); cwd = worktree root; **timeout kills the tree** (script spawns a child that would outlive the parent; assert the grandchild pid is gone after the result — pid via marker file); **cancellation** kills the tree and raises `OperationCanceledException`; capture cap marker (>1M chars producer, bounded memory); empty stdout case; description names the shell.
- Both cultures (suite runs de-AT); tests run on all three CI runners (card gate); Windows end-to-end relies on the runner's Git Bash (present on windows-latest) and is verified in CI, flagged in the report.

## Out of scope

`settings.json` wiring + `/shell` (T-16), streaming UI display (TUI cards), stdin forwarding, PTY, background processes, sandboxing.
