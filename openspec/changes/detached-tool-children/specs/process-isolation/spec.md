## ADDED Requirements

### Requirement: Tool children run with no terminal

Every process a tool starts SHALL run with no control over Lunate's terminal. On POSIX the child SHALL be spawned in a new session (`POSIX_SPAWN_SETSID`) so it has no controlling terminal and cannot open `/dev/tty`; its stdin SHALL be `/dev/null` when no line handler answers it, and the line-pump answer pipe when one does; its stdout and stderr SHALL be captured pipes, never the session's tty. On Windows the child SHALL be started with redirected stdio (`Process.Start` with `UseShellExecute: false`), so stdin is a pipe that reads as end-of-file and no console input handle exists. A child therefore can neither consume the session's keystrokes (a masked secret prompt included) nor reconfigure the session's raw mode, and the interactive prompt stays usable throughout a tool call.

#### Scenario: A tool child cannot touch the session tty
- **GIVEN** the interactive session is running in raw mode
- **WHEN** a `bash` tool call starts a child that asks for the terminal
- **THEN** the child sees no controlling terminal (`tty` is without a name), its stdin is not a terminal, and the session's own termios (`stty -a`) is identical while the call runs, after cancellation and after quit

#### Scenario: Keys typed during an interactive child stay in the session
- **GIVEN** a tool call has started a child that wants terminal input
- **WHEN** the user types or presses approval keys
- **THEN** the child receives nothing from the session's tty; the keystrokes stay in the session's own key reader

### Requirement: Cancellation kills the detached tree

Cancelling or timing out a tool process SHALL end the whole tree: the runner SHALL kill the spawned child and every descendant it started (children before parents) on cancellation and on timeout, then reap it, so no grandchild survives an interrupted call. This composes with the isolation above: the command session, its prompts and any dead-end spinner are all part of the killed tree.

#### Scenario: Cancel during a long call leaves nothing running
- **GIVEN** a `bash` call has started a long-running command tree
- **WHEN** the user presses `Esc` while the call runs
- **THEN** the turn stops, the process tree is gone from the process table, and the input line accepts the next command without a restart

### Requirement: The key-route diagnostic is secret-safe

`LUNATE_INPUT_TRACE` SHALL trace the keys the interactive session reads when set. A value of `1` SHALL trace to `<state root>/diagnostics/input-trace.log`; any other value SHALL name a path that canonicalises to inside the state root and SHALL be refused otherwise. A trace line SHALL record only the key's kind, its modifiers, the routing decision the router made, the run/approval state at that key, and a UTC timestamp. Printable characters and paste content SHALL always be reduced to a `chars=N` count — a credential typed into a masked prompt or carried by a paste leaves no bytes in the file. On POSIX the trace file SHALL be owner-only (0600), UTF-8 and LF.

#### Scenario: A paste carrying a secret leaves no bytes
- **GIVEN** `LUNATE_INPUT_TRACE=1`
- **WHEN** the user pastes text that contains a credential
- **THEN** the trace names the paste and its character count, and no byte of the secret appears in the trace file

#### Scenario: An `Esc` that never reached a turn is still in the trace
- **GIVEN** `LUNATE_INPUT_TRACE=1` and a report that `Esc` did not stop a run
- **WHEN** the session writes the trace for every key it read
- **THEN** the trace shows the `Esc` with the routing decision and the run state, so the report has the route it missed
