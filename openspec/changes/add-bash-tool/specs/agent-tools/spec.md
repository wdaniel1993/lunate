## ADDED Requirements

### Requirement: Bash tool
The `bash` tool SHALL run a single non-interactive command through the platform's resolved shell, in the workspace root, with stdout and stderr captured separately, output decoded as UTF-8, both streams bounded with an explicit cap marker, and the exit code surfaced in the result (culture-invariant). The tool SHALL be marked `Execute` risk so the approval policy applies, its description SHALL name the resolved shell and dialect, and a missing shell SHALL produce a clear error result rather than a partial run. On timeout (default 120 s) or cancellation the process tree SHALL be killed with `Process.Kill(entireProcessTree: true)`; cancellation SHALL rethrow `OperationCanceledException`; `IsError` SHALL be true for non-zero exits, timeouts and spawn failures.

#### Scenario: Shell resolution follows the platform table
- **GIVEN** a Windows machine with Git Bash installed
- **WHEN** the shell is resolved
- **THEN** Git Bash wins with bash-dialect invocation, `C:\Windows\System32\bash.exe` is never selected, and the order falls through pwsh 7, Windows PowerShell, cmd only when earlier shells are absent

#### Scenario: A timeout kills the whole tree
- **GIVEN** a command that spawns a long-lived child
- **WHEN** the timeout expires
- **THEN** the result reports the timeout and neither the command nor its child remains alive

#### Scenario: Cancellation kills the tree and propagates
- **GIVEN** a running command
- **WHEN** the call's token is cancelled
- **THEN** the tree is killed and an `OperationCanceledException` is raised

#### Scenario: UTF-8 and exit codes round-trip
- **GIVEN** a command emitting non-ASCII text and exiting non-zero
- **WHEN** it runs
- **THEN** the output carries the text intact and the result shows the exit code with `IsError` true

#### Scenario: Output is bounded
- **GIVEN** a command producing more than the capture cap
- **WHEN** it runs
- **THEN** the captured output ends with the cap marker and memory stays bounded
