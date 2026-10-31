# protocols Specification

## Purpose
Lunate speaks external agent tool protocols from `Lunate.Protocols`. v1 is an MCP client: stdio servers start lazily, their tools become Lunate tools (`server__tool`, risk `Execute` so the approval policy applies), tool-list changes refresh, and calls honor cancellation, timeouts and server failures without ever crashing a run. Remote transports and ACP follow the same split: protocol I/O here, semantics in the core.

## Requirements

### Requirement: MCP client with lazy servers

The MCP client SHALL connect stdio servers configured by options (name, command, args, env, cwd) and SHALL start a server process only on first use — never at construction or startup. Server tools SHALL be wrapped as `ITool`s named `server__tool` with the MCP description and input schema, carrying risk `Execute` so the approval policy applies, a namespace named after the server, and the exposure configured for the server or the individual tool (default `Direct`); overrides SHALL match the server's own tool name and an override naming no listed tool SHALL be logged. Disposing the host SHALL stop the process idempotently and leave no orphans; setup and process failures SHALL produce clear, actionable errors naming the server.

#### Scenario: Nothing starts before first use

- **GIVEN** a configured server that marks its start
- **WHEN** the host is constructed but no tool is requested
- **THEN** no process has started; the first tool listing starts it exactly once

#### Scenario: Tools arrive wrapped and prefix-namespaced

- **GIVEN** a started server with known tools
- **WHEN** tools are listed
- **THEN** each tool appears as `server__tool` with its schema and risk `Execute`

#### Scenario: Exposure and namespace follow the options

- **GIVEN** a started server configured with a per-tool exposure override
- **WHEN** tools are listed
- **THEN** each tool carries the server's namespace and the configured exposure, with the override applied to its named tool

#### Scenario: Dispose leaves no process behind

- **GIVEN** a started server
- **WHEN** the host is disposed (twice, for good measure)
- **THEN** the child process is gone and later calls fail cleanly

### Requirement: MCP calls honor cancellation, timeouts and failures
Tool calls SHALL propagate the turn's cancellation as `OperationCanceledException`; a call exceeding the configured timeout SHALL return an error result stating the timeout; a crashing tool or dead server SHALL return error results naming the server — a broken server SHALL never crash Lunate, and the client SHALL remain usable afterwards. Tool-list-change notifications from the server SHALL refresh the wrapped tool set and surface it to the host's callback. The client SHALL negotiate a protocol revision whose tool-list changes are broadcast (pinned to 2025-11-25 with SDK 2.2.0).

#### Scenario: A slow call is cancelled with the turn
- **GIVEN** a started server with a slow tool
- **WHEN** the call's token is cancelled during execution
- **THEN** `OperationCanceledException` is raised and the server observes the cancellation

#### Scenario: A hanging call times out as a tool error
- **GIVEN** a short call timeout and a slow tool
- **WHEN** the call runs longer than the timeout
- **THEN** an error result explains the timeout — no exception escapes

#### Scenario: Crashing and dying servers become tool errors
- **GIVEN** a server whose tool throws, or a server process killed mid-session
- **WHEN** tools are called
- **THEN** each failure is an error result that names the server, and subsequent calls still behave cleanly

#### Scenario: Tool list changes refresh the set
- **GIVEN** a started server
- **WHEN** the server registers a new tool and notifies a list change
- **THEN** the host's callback receives the updated wrapped tool set including the new tool

### Requirement: ACP server over stdio

`lunate --acp` SHALL speak the Agent Client Protocol over stdio so editors drive Lunate as their agent, behind an `IAcpServer` seam (`RunAsync(input, output, createHarness, ct)`) that keeps a later SDK switch cheap. The server SHALL implement `initialize` (protocol version 1, honest capabilities, negotiate down, log unsupported), session creation (a fresh harness per ACP session; the factory receives the session context — cwd, the client-backed approver, and the client file access when offered), `session/prompt` (one run; text and resource-link content blocks — resource links map to model-readable text, other kinds are logged and skipped; streaming `session/update` notifications mapped from `AgentEvent`s per the guide's event table (reasoning events map to `agent_thought_chunk` updates); the response carries the stop reason) and `session/cancel` (cancels the in-flight run; the prompt response reports the cancellation; no updates follow it). Child-run (subagent) events SHALL NOT be passed to ACP clients raw: in this version the server filters them out of `session/update` (approvals continue to surface through the client-backed approver). Nothing but protocol frames may be written to stdout; logs go to stderr. The mode SHALL be mutually exclusive with print mode and the TUI (usage error, exit 2). The server SHALL be tested with an in-process client over a pipe pair.

#### Scenario: Initialize negotiates the protocol version

- **GIVEN** a client sending `initialize` with protocol version 1
- **WHEN** the server responds
- **THEN** the response carries version 1 and the implemented capabilities; a higher requested version negotiates down; unsupported capabilities are logged, not fatal

#### Scenario: A prompt streams updates in order and ends with the stop reason

- **GIVEN** a session and a prompt with a text block
- **WHEN** the run streams text and tool events
- **THEN** `session/update` notifications arrive in event order (agent message chunks, tool call, tool call update) and the prompt response carries the stop reason

#### Scenario: Cancel stops the run

- **GIVEN** a running prompt
- **WHEN** the client sends `session/cancel`
- **THEN** the run is cancelled, the prompt response reports the cancellation, and no updates follow it

#### Scenario: stdout carries only the protocol

- **GIVEN** `lunate --acp` started over stdio
- **WHEN** any diagnostics occur
- **THEN** stdout contains only protocol frames and every log line appears on stderr

#### Scenario: The mode conflicts are rejected

- **GIVEN** `lunate --acp` combined with `-p` or a prompt
- **WHEN** it starts
- **THEN** it exits with a usage error (exit 2) and speaks no protocol

#### Scenario: Child events never reach the client raw

- **GIVEN** a run that spawns a subagent streaming its own text and tool events
- **WHEN** the client reads `session/update`
- **THEN** no child-run event appears on the wire, while the parent's own updates and the spawn result do

#### Scenario: Reasoning maps to thought chunks

- **GIVEN** an ACP session whose run streams reasoning
- **WHEN** the client reads `session/update`
- **THEN** the reasoning events arrive as `agent_thought_chunk` updates in event order

### Requirement: ACP approvals, editor file system and resource links

In ACP mode, tool approvals SHALL compose the configured policy with the client's `session/request_permission`: only `ReadOnly` calls SHALL run without prompting; every other call — a write under any policy, and every command — SHALL ask the editor with allow-once, allow-always and reject options — allow-once executes, allow-always executes and is remembered per tool for the session, reject declines as a tool error, and a cancel or request error resolves as a decline. Every client round trip SHALL be bounded: a file request that outlives its timeout SHALL fail as an I/O error, and a permission request that outlives its timeout SHALL decline (both logged) — a stalled editor degrades, it does not hang. A session cancel SHALL also stop an in-flight file round trip: the call fails promptly instead of waiting out its timeout, and the run ends cancelled. A timed-out request that is abandoned SHALL be observed, so a late failure cannot surface as an unobserved exception. A missing file SHALL surface as a not-found failure and other client failures as I/O errors — never masked into one another. Commands SHALL prompt under every policy. When the client advertises the file-system capability, the read, write and edit tools SHALL route file access through the client's `fs/read_text_file`/`fs/write_text_file` so editor buffers are respected (the client owns byte-order marks); without the capability they SHALL use the local disk. Prompt `ResourceLinkContent` blocks SHALL map to model-readable text: a `file://` URI inside the workspace becomes `@<relative path>`, anything else becomes `<name> (<uri>)`, in prompt order.

#### Scenario: Policy-allowed calls run unprompted

- **GIVEN** an ACP session under the ask policy and a read-only tool call
- **WHEN** the run executes it
- **THEN** no permission request is sent to the client

#### Scenario: A write asks the editor and allow-once executes

- **GIVEN** an ACP session under the ask policy and a write tool call
- **WHEN** the client answers allow-once
- **THEN** the request carried the tool call information and the write executed

#### Scenario: A rejection declines as a tool error

- **GIVEN** a permission request
- **WHEN** the client answers reject
- **THEN** the call is declined and surfaces as a tool error result, as a local decline does

#### Scenario: Allow-always is remembered for the session

- **GIVEN** a permission request answered allow-always
- **WHEN** the same tool is called again in the session
- **THEN** it runs without a further request

#### Scenario: Commands always prompt

- **GIVEN** an ACP session under any policy and a command tool call
- **WHEN** it runs
- **THEN** the client receives a permission request

#### Scenario: Cancelling during a request declines it

- **GIVEN** a permission request in flight
- **WHEN** the session is cancelled
- **THEN** the request resolves as a decline and nothing hangs

#### Scenario: Editor buffers are respected

- **GIVEN** a client advertising the file-system capability whose buffer differs from disk
- **WHEN** a read executes
- **THEN** it returns the client's buffer content, and a write goes to the client with the local file untouched

#### Scenario: Without the capability the disk is used

- **GIVEN** a client without the file-system capability
- **WHEN** file tools run
- **THEN** they read and write the local disk as before

#### Scenario: Resource links become readable text

- **GIVEN** a prompt containing a resource link to a workspace file and one to an external URI
- **WHEN** the prompt reaches the model
- **THEN** the first appears as `@<relative path>` and the second as `<name> (<uri>)`, in order

#### Scenario: A stalled file round trip fails as an error

- **GIVEN** a client that never answers a file request
- **WHEN** a file tool runs
- **THEN** it fails as an I/O error after the timeout instead of hanging

#### Scenario: A stalled permission request declines

- **GIVEN** a permission request the client never answers
- **WHEN** its timeout passes
- **THEN** the call is declined (logged) and the run continues with the decline

#### Scenario: Missing files are not masked

- **GIVEN** a client whose read reports the file missing
- **WHEN** a file tool runs
- **THEN** the result says the file was not found, while other client failures surface as I/O errors

#### Scenario: Cancelling during a file round trip stops it promptly

- **GIVEN** a client that has not yet answered a file request
- **WHEN** the session is cancelled
- **THEN** the file call fails promptly — well before its timeout — and the prompt response reports the cancellation

#### Scenario: An abandoned request is observed

- **GIVEN** a file request that timed out and was abandoned
- **WHEN** the client's late answer fails the request
- **THEN** the failure is observed and does not surface as an unobserved task exception

#### Scenario: Auto-edit still asks the editor for a write
- **GIVEN** an ACP session under the auto-edit policy and a write tool call
- **WHEN** the run executes it
- **THEN** a permission request is sent to the client

### Requirement: MCP servers from mcp.json

The CLI SHALL read stdio server configurations from five files, each a `mcpServers` map keyed by server name, each entry carrying `command` (required), `args`, `env`, optional `cwd` and optional `callTimeoutSeconds`; unknown fields SHALL be ignored. The files SHALL be: the user files `~/.lunate/mcp.json` and `~/.agents/mcp.json`, and the worktree's `.lunate/mcp.json`, `.agents/mcp.json` and repo-root `.mcp.json`. Entries SHALL merge by server name with the precedence `.lunate/mcp.json` > `.agents/mcp.json` > `.mcp.json` within each scope and every project file over every user file; each shadowed definition SHALL be logged naming the server and both source files. `command`, `args` and `env` values SHALL support Claude Code-compatible expansion — `${VAR}` to the variable's value, `${VAR:-default}` to the value or the default — computed when the file loads; a variable that is unset and has no default SHALL leave the literal text in place and log a warning naming the server and the variable, surfaced next to the server in `/mcp`. Because a project file runs repository-provided commands, each of the three project files SHALL require a one-time trust decision per repository identity, re-prompted when the file changes (the project-extension discipline), and each server defined by a project file SHALL additionally require a one-time approval per definition before it can start — keyed by the repository identity and a canonical hash of its raw definition as written, re-prompted when that definition changes. Both hashes SHALL be computed over the raw definitions as written — before any `${VAR}` expansion, canonically serialized — so they are machine-independent and never capture expanded values or secrets. Non-interactive modes SHALL refuse an untrusted project file or an unapproved project server with a logged hint naming the approval paths — an interactive session or the `lunate trust` command — and continue with the rest. The files SHALL be read when a session starts — never during CLI startup; `--version`, `--help`, `--discover` and flag parsing SHALL NOT read them; the `lunate trust` command reads the project files only to evaluate and record trust, never to start servers. Only servers listed in the files SHALL ever start. Each configured server SHALL follow the existing lazy-server contract: nothing starts before its first listing, tools are named `server__tool` with the server's namespace and risk `Execute`, and a server that fails to start or list SHALL produce an actionable notice naming the server while the session continues without its tools. A malformed entry SHALL be reported naming the server and field and the server skipped; a malformed or unreadable file SHALL be reported with its path and the session SHALL continue without its servers.

#### Scenario: A configured server works in a session

- **GIVEN** a `mcp.json` configuring a working stdio server
- **WHEN** an interactive session starts and the model calls one of its tools
- **THEN** the tool is listed as `server__tool`, the call round-trips through the approval policy and the result renders in the session

#### Scenario: Nothing starts for non-session commands

- **GIVEN** a `mcp.json` whose server would write a marker when started
- **WHEN** `lunate --version`, `--help` or `--discover` runs
- **THEN** the file is not read and no server process starts

#### Scenario: A broken entry is actionable and isolated

- **GIVEN** a `mcp.json` with one valid and one malformed entry
- **WHEN** a session starts
- **THEN** the malformed entry is reported naming the server and field, the valid server still works, and the session is unaffected

#### Scenario: A failing server never breaks the session

- **GIVEN** a configured server that cannot start
- **WHEN** a session starts
- **THEN** a notice names the server, the session continues without its tools, and other tools behave normally

#### Scenario: An untrusted project file prompts once

- **GIVEN** a repository with a project `mcp.json` and no trust record
- **WHEN** an interactive session starts and the prompt is approved
- **THEN** the decision is recorded and a later session does not prompt again; a changed file re-prompts

#### Scenario: A denied project file never starts its servers

- **GIVEN** a project `mcp.json` whose trust is denied
- **WHEN** a session starts
- **THEN** the project servers do not start (a log line says how to approve), and global servers still work

#### Scenario: A project entry overrides a same-named global entry

- **GIVEN** a server name configured in both the global and the trusted project file
- **WHEN** a session starts
- **THEN** the project file's definition is the one used

#### Scenario: MCP calls follow the approval policy

- **GIVEN** a configured working server and the default `ask` policy
- **WHEN** the model calls an MCP tool in print mode
- **THEN** the call is refused with an approval error result; with `--yolo` the same call executes

#### Scenario: The trust command starts nothing

- **GIVEN** a project `mcp.json` whose server would write a marker when started
- **WHEN** `lunate trust --yes` runs
- **THEN** the trust is recorded and no server process starts

#### Scenario: A standard-location server is read and wins over the root file

- **GIVEN** a worktree whose `.agents/mcp.json` and root `.mcp.json` define the same server name, both trusted and approved
- **WHEN** a session starts
- **THEN** the `.agents/mcp.json` definition is used and a collision line names the server and both files

#### Scenario: Expansion substitutes values and defaults

- **GIVEN** a trusted project `mcp.json` whose `command`, `args` and `env` reference `${VAR}` and `${VAR:-default}`
- **WHEN** a session starts with `VAR` set and the defaulted variable unset
- **THEN** the server starts with the expanded values

#### Scenario: An unset variable without a default warns and stays literal

- **GIVEN** a trusted project `mcp.json` referencing an unset `${VAR}` with no default
- **WHEN a session starts**
- **THEN** a warning names the server and the variable, and the literal `${VAR}` text is used

#### Scenario: A project server needs its own approval once

- **GIVEN** a trusted project file with a server definition and no per-server approval
- **WHEN** an interactive session starts and the per-server prompt is approved
- **THEN** the approval is recorded for that definition in this repository, a later session does not ask again, another repository with the same definition still asks, and a changed definition re-prompts

#### Scenario: An unapproved project server is refused non-interactively

- **GIVEN** a trusted project file whose server has no per-server approval
- **WHEN** a print-mode session starts
- **THEN** the server does not start, a log line names the approval paths, and the rest of the session works

### Requirement: MCP form elicitation uses the shared question interaction

The MCP client SHALL advertise `elicitation.form` and handle MCP `elicitation/create` form requests through the same question interaction used by `ask_user`. It SHALL accept the protocol's `message` and flat-object `requestedSchema`, including supported primitive fields, single-select enums and multi-select enum arrays; answers SHALL be validated against the requested schema and returned using MCP's `accept`, `decline` or `cancel` response shape. It SHALL identify the requesting MCP server in the user-facing question and SHALL NOT create a second picker implementation. If no user interaction is available, it SHALL return `cancel` immediately and record the unanswered outcome in the enclosing tool result so the model can state an assumption and continue. V1 SHALL NOT advertise URL-mode elicitation.

#### Scenario: A server's form request is answered through the shared picker

- **GIVEN** an MCP server sends `elicitation/create` with `mode: "form"`, a message and a flat `requestedSchema` containing an enum field
- **WHEN** the user selects a valid value and submits
- **THEN** the shared question interaction returns an `accept` response whose content validates against the requested schema and identifies the requesting server

#### Scenario: A multi-select form returns its selected values

- **GIVEN** an MCP form field whose schema is an array of strings constrained by an enum
- **WHEN** the user selects multiple values and submits
- **THEN** the client returns those values as an array in the accepted content

#### Scenario: A skipped MCP form is cancelled, not approved

- **GIVEN** an MCP server's form elicitation is open
- **WHEN** the user presses Esc
- **THEN** the client returns an MCP `cancel` response without content and no tool approval is granted

#### Scenario: An unattended MCP form does not block

- **GIVEN** an MCP server requests form elicitation during a print or unattended run
- **WHEN** no user-input mechanism is available
- **THEN** the client immediately returns `cancel` without content, records `unanswered: no user present` in the enclosing tool result, and the model is directed to state an assumption and continue

#### Scenario: URL-mode elicitation is not advertised or presented as a form

- **GIVEN** an MCP server connected to Lunate
- **WHEN** it inspects client capabilities or sends an unsupported URL-mode request
- **THEN** Lunate advertises form mode only and refuses the unsupported request as invalid parameters without opening a URL

### Requirement: ACP questions use only a non-approval input mechanism

In ACP mode, a question SHALL use a client mechanism explicitly intended for user input when the client advertises one. Lunate SHALL NOT use `session/request_permission` to collect an answer. If no such mechanism is available, the question SHALL return immediately as `unanswered: no user present`.

#### Scenario: An ACP user-input capability receives the question

- **GIVEN** an ACP client advertises a mechanism for non-approval user input
- **WHEN** `ask_user` runs
- **THEN** the question is sent through that mechanism and its answer is returned as information, not permission

#### Scenario: Permission requests are not reused for questions

- **GIVEN** an ACP client advertises `session/request_permission` but no user-input mechanism
- **WHEN** `ask_user` runs
- **THEN** no permission request is sent and the tool immediately returns `unanswered: no user present`
