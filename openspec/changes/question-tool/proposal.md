# Question Tool

## Why

The model currently has no safe way to ask the user to make a decision that only the user can make, and MCP servers cannot request structured user input through Lunate. One shared question contract and picker lets both paths ask without confusing an answer with permission to run a tool.

## What Changes

- Add the `ask_user` tool for 1–4 concise questions, each with 2–4 labelled options, short option descriptions, optional recommended option, single or multiple selection, and free text.
- Reuse the question picker for MCP `elicitation/create` form requests using the protocol's `message` and flat `requestedSchema` shape; implement Lunate's MCP-client elicitation capability without a second picker.
- Route interactive questions through the existing `SelectList` interaction and approval arming discipline; return an immediate `unanswered: no user present` result in print and unattended runs, and use an ACP client's separate non-approval input mechanism when available.
- Keep `ask_user` unavailable to subagents in v1; do not bubble child questions to the parent user's prompt.
- Record each request and answer/outcome as a pair in the existing tool-call/result session messages, interactive transcript and export; preserve the current session schema and its golden files.
- Keep tool approval separate: a question answer is information only and cannot approve any tool call.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `agent-tools`: define `ask_user`, its description and answer semantics, subagent exclusion, and the boundary between answers and tool approvals.
- `protocols`: support MCP `elicitation/create` form requests through the same question interaction, and route ACP questions only through a client mechanism for user input (never `session/request_permission`).
- `tui`: render and operate multi-question selection and free-text entry with `SelectList` behavior, approval-style key arming, skip semantics and narrow-width layouts.
- `print-mode`: return immediately with `unanswered: no user present` for questions when no user interaction is available, without blocking or writing question output to stdout.
- `agent-sessions`: preserve the question/answer-or-unanswered pair in the existing tool-call/result message records for session replay and export; no session-format migration.

## Impact

- **Agent and protocols:** `Lunate.Agent` tool registration/execution and `Lunate.Protocols` MCP client elicitation handling; no new NuGet package.
- **Terminal:** `Lunate.Tui` shared question presentation and key handling, preserving existing picker and approval semantics.
- **Persistence and presentation:** existing session message records, interactive transcript, and both export forms; no session-format or golden fixture change.
- **Approval boundary:** answers never alter approval policy, remembered grants, or tool-call approval state.
