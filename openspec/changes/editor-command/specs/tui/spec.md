## MODIFIED Requirements

### Requirement: Slash commands

A submitted line whose first non-space character is `/` SHALL dispatch as a client command and never reach the model: `/model` opens the model picker (`/model <id>` switches directly when the id is in the catalog), and when the current model is an extension-registered or virtual model the picker's current-model line and `/model` SHALL show its last routed resolution (`auto → deep`); `/new` starts a fresh session, `/name <title>` names the current session and bare `/name` reports the stored name, `/session` reports the current session's path, id, message count and token totals, `/resume` opens the session picker, `/compact` requests an explicit out-of-run compaction, `/thinking <level>` sets the session's reasoning level (one of `off`/`low`/`medium`/`high`; `/thinking` alone prints the current level and the active model's mapping), `/mcp` lists the configured and extension-registered MCP servers with their status and source (the config file path it came from, or `extension '{id}'`), with a name collision shown as the configured server winning and the registration marked shadowed, a server whose definition references an unset environment variable without a default shown with that missing-variable warning next to it, and never printing environment values or headers, `/extensions` lists every discovered extension — failed first, then trust-denied, then loaded, each group by id — as one segment per extension: `id version (scope) state`, with loaded ones carrying their load time and registered tool, command, hook and service counts (a field differing from the manifest reads `(N declared)`, a load above 500 ms reads `(slow)`), trust-denied ones reading `trust-denied` with the manifest's declared counts, failed ones reading `failed` followed by the message's first sentence and ` — see the log` — reading `extensions: none configured` when there are none and `extensions disabled (--no-extensions)` when extensions are disabled, `/trust` lists this repository's trust records (`/trust clear` clears them, so the next load asks again), `/editor` opens the draft in the external editor — the same round-trip `Ctrl+G` runs, so the command is the way in when the terminal does not deliver the key or a keybindings file claims it — `/skills` lists the discovered skills with their scope and state, `/skill:<name> [args]` invokes the named skill by submitting its wrapped body as a user turn through the normal submission path — an unknown name warns instead — `/help` lists the commands grouped (session / model / extensions / files / quit) with one-line descriptions taken from the command registry — no hand-maintained list: a command dispatchable without a description SHALL fail a test — `/quit` quits. Commands SHALL append to the input history and skip the input pipeline; an unknown command SHALL produce a dim notice naming it. While a turn is running, `/new`, `/resume`, `/model` and `/compact` SHALL be refused with a notice; `/quit` SHALL quit at any time (cancelling a running turn). While an approval prompt or a picker is open, no input is submitted, so no command can dispatch.

#### Scenario: Every built-in command dispatches without a model call

- **GIVEN** an idle scripted session
- **WHEN** each built-in command is submitted
- **THEN** it takes its action, the request count is unchanged, and the command text is recalled by Up

#### Scenario: Unknown command

- **GIVEN** an idle session
- **WHEN** `/nope` is submitted
- **THEN** a dim notice names the command, no run starts, and nothing reaches the model

#### Scenario: Busy session refusals

- **GIVEN** a running turn
- **WHEN** `/new`, `/resume`, `/model` or `/compact` is submitted
- **THEN** a notice refuses it and the run is untouched

#### Scenario: /compact without a compactable tail

- **GIVEN** an idle session whose history has nothing older than the kept tail
- **WHEN** `/compact` is submitted
- **THEN** a notice says nothing was compacted

#### Scenario: Commands skip the input pipeline

- **GIVEN** an installed consuming input hook
- **WHEN** a command is submitted
- **THEN** the hook never sees the command text and the command still acts

#### Scenario: /mcp lists configured servers with status

- **GIVEN** an idle session with one started and one failing configured MCP server
- **WHEN** `/mcp` is submitted
- **THEN** a notice lists both servers with their status (started with its tool count; failed with the reason), without a model call

#### Scenario: /mcp lists extension-registered servers with status and source

- **GIVEN** an idle session where an extension registered an MCP server and no configured servers exist
- **WHEN** `/mcp` is submitted
- **THEN** a notice lists the extension-registered server with its status and `extension '{id}'` as its source, without a model call

#### Scenario: /mcp shows a name collision with the configured server winning

- **GIVEN** an idle session with a configured server and an extension registration sharing its name
- **WHEN** `/mcp` is submitted
- **THEN** the notice shows the configured server with its status and source, and the registration as shadowed by the configured source, without a model call

#### Scenario: /mcp shows a crashed server as failed

- **GIVEN** a session whose started MCP server process has exited
- **WHEN** `/mcp` is submitted
- **THEN** the notice shows the server as failed with `the server process exited`, and a notice named the server

#### Scenario: /mcp never prints environment values or headers

- **GIVEN** an idle session whose servers carry environment values
- **WHEN** `/mcp` is submitted
- **THEN** the notice contains neither the environment values nor any header names

#### Scenario: /trust lists and clears this repository's records

- **GIVEN** a repository with recorded trust decisions
- **WHEN** `/trust` is submitted
- **THEN** a notice lists them, and after `/trust clear` the records are gone and a later load asks again

#### Scenario: The picker shows routing for a virtual model

- **GIVEN** a session whose model is a virtual model with a recorded last resolution
- **WHEN** the model picker opens
- **THEN** its current-model line shows `auto → deep`

#### Scenario: /mcp shows a missing-variable warning next to the server

- **GIVEN** an idle session whose server definition references an unset `${VAR}` with no default
- **WHEN** `/mcp` is submitted
- **THEN** the server's entry carries the missing-variable warning, and no model call happens

#### Scenario: /extensions lists the discovered extensions with scope, state and contributions

- **GIVEN** an idle session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts — and no model call happens

#### Scenario: /extensions marks a slow load and truncates a failed message

- **GIVEN** an idle session with a loaded extension whose load exceeded the threshold and a failed extension with a multi-sentence message
- **WHEN** `/extensions` is submitted
- **THEN** the loaded segment carries `(slow)` and the failed segment carries the message's first sentence followed by ` — see the log`

#### Scenario: /extensions with no extensions discovered or disabled

- **GIVEN** an idle session with no extensions discovered (or one started with `--no-extensions`)
- **WHEN** `/extensions` is submitted
- **THEN** the notice reads `extensions: none configured` (or `extensions disabled (--no-extensions)`), and no model call happens

#### Scenario: /skills lists discovered skills with scope and state

- **GIVEN** an idle trusted session with one project skill and one `disable-model-invocation` user skill
- **WHEN** `/skills` is submitted
- **THEN** a notice lists both with their scopes and the command-only marker, and no model call happens

#### Scenario: /skill:name invokes the skill

- **GIVEN** an idle trusted session with skill `greek` discovered
- **WHEN** `/skill:greek` is submitted
- **THEN** the skill body reaches the model as a wrapped user turn and exactly one run starts

#### Scenario: /skill:unknown warns without a run

- **GIVEN** an idle session without a skill named `ghost`
- **WHEN** `/skill:ghost` is submitted
- **THEN** a dim notice reads `unknown skill: ghost (see /skills)` and no run starts

#### Scenario: /help lists every command with a description

- **GIVEN** an idle session
- **WHEN** `/help` is submitted
- **THEN** the listing shows every built-in command grouped (session / model / extensions / files / quit), each with a one-line description, and no command appears without one

#### Scenario: /thinking sets and reports the level

- **GIVEN** an idle session
- **WHEN** `/thinking high` then `/thinking` are submitted
- **THEN** the level is set to `high`, the footer shows `think:high`, and the bare command prints the current level with the active model's mapping — without a model call

#### Scenario: /editor reaches the editor round-trip

- **GIVEN** an idle session and a scripted editor that writes two lines
- **WHEN** `/editor` is submitted
- **THEN** the editor child ran on the temp file, the prompt holds the editor's text afterwards, and no run starts

### Requirement: External editor for the draft

`Ctrl+G` SHALL open the current draft in an external editor and put the result back in the prompt: the draft SHALL be written to a temporary file as UTF-8 without a BOM (owner-only permissions on POSIX), the live area SHALL be suspended and the terminal returned to cooked mode for the child, and the command SHALL be `$VISUAL`, else `$EDITOR`, else `vi` on POSIX and `notepad` on Windows. A variable's value SHALL be split on whitespace into a program and its arguments, with no shell and no quote interpretation. On a zero exit the live area SHALL resume, the file SHALL be read back with CRLF normalised to LF and exactly one trailing newline removed, the draft SHALL become that text and the cursor SHALL sit at its end. On a non-zero exit, a launch failure or a read-back that fails, the draft, the cursor and the stash SHALL be untouched, the live area SHALL resume, and one notice SHALL name what failed — the variable and the value it tried, the exit code, or the read that failed. A variable that is set but cannot be launched SHALL be reported, never silently replaced by the next candidate. An empty draft SHALL still open an empty buffer, and an editor that leaves the buffer empty SHALL leave the draft empty. While the child runs the session SHALL read no keys and accept no input: a key typed at the editor belongs to the editor. The same round-trip SHALL be reachable as the `/editor` command: it SHALL act on the prompt as it stands when the command dispatches — a submitted command clears the line first, so a bare `/editor` opens an empty buffer — and it SHALL add no editor behaviour of its own, so the resolution, the failure notices and the temporary file's lifecycle are the ones above. If the draft changed while the editor was open, the editor's text SHALL become the prompt — it is what the user just deliberately wrote — the draft that changed meanwhile SHALL be written to the stash slot, and one notice SHALL name that slot, so neither text is lost. The temporary file SHALL be deleted on every exit path, exceptions included, so no draft text is left behind in the temporary directory.

#### Scenario: A multi-line draft round-trips

- **GIVEN** a draft reading `line one` with the cursor after `line`, and an editor that leaves `line one\r\nline two\r\n`
- **WHEN** the editor exits 0
- **THEN** the draft is `line one\nline two`, the cursor is at its end, and the input box repaints at its new height

#### Scenario: An empty draft opens an empty buffer

- **GIVEN** an empty prompt
- **WHEN** `Ctrl+G` runs an editor that writes two lines
- **THEN** the editor was started on an empty file and the prompt holds those two lines afterwards

#### Scenario: The editor resolves in order

- **GIVEN** `VISUAL` and `EDITOR` both set, then only `EDITOR`, then neither
- **WHEN** `Ctrl+G` runs
- **THEN** the child is `$VISUAL`, then `$EDITOR`, then `vi` on POSIX or `notepad` on Windows, and a value carrying arguments is split into a program and its arguments with no shell in between

#### Scenario: A missing editor binary leaves the draft alone

- **GIVEN** `VISUAL` naming a program that does not exist and a draft reading `draft text`
- **WHEN** `Ctrl+G` is pressed
- **THEN** the draft and the cursor are unchanged, the live area is back, and one notice names `VISUAL` and the value it tried

#### Scenario: A non-zero exit leaves the draft alone

- **GIVEN** an editor that writes text and then exits 1
- **WHEN** `Ctrl+G` is pressed
- **THEN** the draft and the cursor are as before the command, one notice carries the exit code, and the temporary file is gone

#### Scenario: The child owns the terminal

- **GIVEN** a live frame with a running turn and an editor that writes to the terminal
- **WHEN** `Ctrl+G` runs it
- **THEN** the live area paints nothing while the child holds the terminal, the terminal is in cooked mode for the child, and the frame repaints in full on exit

#### Scenario: No key reaches the session while the editor runs

- **GIVEN** an editor that stays open for several frames
- **WHEN** keys are typed at it
- **THEN** the session reads none of them, and the draft and the history are unchanged by them

#### Scenario: The editor's text wins over a draft that changed meanwhile

- **GIVEN** a draft that changed while the editor was open
- **WHEN** the editor exits 0
- **THEN** the editor's text is in the prompt, the draft that changed meanwhile is in the stash slot, and one notice names the slot the earlier text went to

#### Scenario: The temporary file never stays behind

- **GIVEN** a successful run, a non-zero exit and a launch that throws
- **WHEN** each command returns
- **THEN** the temporary file is deleted in all three, and no draft text is left in the temporary directory

#### Scenario: The catalog decides Ctrl+G

- **GIVEN** the binding catalog
- **WHEN** `Ctrl+G` is routed in a prompt context
- **THEN** it produces the editor intent and no other row matches, a second row for the same pattern in an overlapping context is rejected by the catalog's overlap check, and an extension or keybindings file that wants `Ctrl+G` replaces that row rather than appending one

#### Scenario: The command reaches the same round-trip

- **GIVEN** an idle session and an editor that writes a line and exits 1
- **WHEN** `/editor` is submitted
- **THEN** the draft is untouched, one notice carries the exit code, the temporary file is gone, and no run starts
