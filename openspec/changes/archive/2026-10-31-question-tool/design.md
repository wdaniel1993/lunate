# Question Tool Design

## Context

`Lunate.Agent` owns the hand-authored `ITool` contract and loop; `Lunate.Protocols` hosts MCP and ACP adapters; the interactive host composes those with `Lunate.Tui`. The tool contract follows ADR-0012 and the loop-owned execution boundary in ADR-0003/ADR-0013. `SelectList` already provides a data-only picker model, selection, and escaped rendering; keep the existing TUI/input architecture of ADR-0007 and ADR-0019. The approval prompt has a separate arming rule: buffered keys and keys in its first 300 ms cannot decide it. Session history stores assistant/tool messages under the format contract in ADR-0015, and the interactive transcript and JSONL/HTML exporters already render those messages. ACP remains behind the SDK boundary recorded in ADR-0020.

MCP 2025-11-25 form elicitation uses `elicitation/create` with a human-readable `message` and a flat `requestedSchema` object. Its supported properties include primitive fields, enums, enum arrays, titles, descriptions and defaults. Lunate can advertise form elicitation without advertising URL mode. The current ACP contract has permission requests, not a separate non-approval user-input request; the permission path must not be repurposed.

The requested `openspec/project.md` is not present in this checkout. Capability names below come from the existing `openspec/specs/` directories and the OpenSpec instructions.

## Goals / Non-Goals

**Goals:**

- Give the model a bounded, structured `ask_user` tool for user-owned decisions.
- Use one normalized question interaction for native questions and MCP form requests.
- Keep answers distinct from tool approval and make no-user paths immediate and non-blocking.
- Preserve question/answer pairs in existing session messages, transcript and exports without a session schema change.
- Keep v1 unavailable to subagents.

**Non-Goals:**

- MCP URL-mode elicitation, secret collection, or a browser flow.
- Bubbling a subagent's question to its parent.
- Reusing ACP `session/request_permission` for non-approval input.
- Adding packages, changing the agent loop contract, or changing the session format.

## Decisions

### One normalized question interaction, two input adapters

Keep `ask_user` as a normal hand-authored `ITool` and normalize its bounded questions into the same question data that the MCP client builds from `elicitation/create`. The native tool accepts 1–4 question fields with short headers, 2–4 label/description options, selection mode, an optional recommended label and a free-text path. MCP requests map their `message` and flat `requestedSchema` fields into that model; enum/oneOf titles become labels, array enums become multi-select choices, and property titles/descriptions supply field context. Schema defaults are suggestions, not accepted answers. Responses are checked against an MCP request's schema before being returned. Do not add private JSON Schema keywords to MCP messages.

MCP's standard schema has field-level descriptions but no dedicated description on each enum choice. Native `ask_user` retains and renders every option's one-line description; an MCP-origin enum uses the description provided by its field and its standard enum labels. Native free text is always available; MCP free text is accepted only when the requested schema permits it. This preserves protocol validity instead of sending an out-of-schema "Other" value.

The MCP client advertises `elicitation.form` only. A server that sends a form request uses the shared picker and receives the standard `accept`, `decline` or `cancel` response. V1 does not advertise URL mode or open a URL. Identify the requesting server in the prompt. Do not infer that a schema default or a recommended option is consent.

### Keep question input separate from approval

The `ask_user` tool invokes a user-question interaction directly; it does not ask the approver for permission to ask. Its result carries information only. Later calls to write, edit or execute tools continue through the existing approval policy and prompt, with no state or grant copied from the question response. ACP may use only a client capability expressly meant for user input; `session/request_permission` remains exclusively an approval protocol. If ACP has no such capability, return `unanswered: no user present` immediately.

Do not expose `ask_user` to child runs. Bubbling a child's question to the parent user is the alternative, but is rejected for v1: it would require parent-run question ownership, ordering and cancellation semantics that the current subagent seam does not provide. Make the tool unavailable in the child registry; do not queue or forward it.

### Reuse the TUI picker and arming discipline

Adapt the normalized request to `SelectList` instead of adding another picker. Present one question at a time to keep the header, descriptions and response controls legible. Arrows move the selection; number keys select or toggle an option; Space toggles multi-select; Enter submits; Esc skips the entire request and discards partial selections. A free-text path is available for every native question and can accompany selected labels. Show a recommended choice first with an explicit marker but do not submit until Enter.

Apply the approval prompt's arming rule to each newly active question: discard buffered input and ignore input for the first 300 ms, including selection, text and submission. Keep answer state in the interaction model across repaint and resize. Render width bands at 120, 80, 60 and 40 columns; 40 columns is the minimum supported band for narrow Termux terminals. Wrap descriptions and trim nonessential context before clipping controls or selected labels.

### No-user behavior is a result, never a wait

The interactive TUI returns an explicit skipped outcome for Esc. Print mode and unattended runs return `unanswered: no user present` immediately, with model-facing guidance to state an assumption and continue. In MCP, map no-user/unavailable interaction to a prompt cancellation response with no content. In ACP, use a separately advertised non-approval input mechanism if present; otherwise return the same unanswered result. Never read stdin to wait for a user in unattended modes, and never write question UI or diagnostics to ACP stdout.

### Record pairs through existing tool messages

For `ask_user`, the assistant tool-call message already carries the request and the tool-result message carries the selected labels/text or unanswered outcome. Keep them as one adjacent, ordered pair in the conversation history and session; do not introduce a session entry or schema version. For MCP, add each elicitation's server-labelled question and answer/outcome in order to the enclosing `server__tool` result so the existing session, transcript and exporters retain the pair. The interactive transcript and HTML export render it as an explicit question/answer pair, with all server- and user-provided text escaped. This is preferred over a new session entry because tool messages already persist and replay direct tool calls, while a new entry would change a pinned format and golden fixtures without adding replay semantics.

### Component view

```text
 Model ──tool call──> Lunate.Agent question tool ──┐
                                                   ├──> Shared question data/result
 MCP server ──elicitation/create──> Protocols ─────┘             │
                                                                 ├──> TUI SelectList
                                                                 └──> tool result / session pair
                                                                      └──> transcript + exports

 Tool call ──> separate approval policy/prompt ──> approved or denied
 Question answer ────────────────────────────────X no approval edge
```

The diagram is a lightweight component/data-flow view: the agent owns tool semantics, the protocol adapter owns MCP wire mapping, the TUI owns interaction rendering, and the existing message/session path owns persistence. The approval route has no input from question answers.

## Risks / Trade-offs

- [A malicious or careless MCP server asks for sensitive form data] -> Advertise only form elicitation, identify the server, follow the MCP restriction against form-mode secret requests, and do not implement a URL/credential flow in v1.
- [An MCP form uses a schema the shared picker cannot represent] -> Support only the documented flat primitive/enum subset; reject unsupported shapes actionably without prompting or sending invalid content.
- [Buffered input answers a question accidentally] -> Reuse the 300 ms approval arming rule and test pre-buffered keys as well as the timed window.
- [A model treats an affirmative answer as permission] -> State the information-only contract in the tool description and verify that a subsequent execute-risk call still reaches a fresh approval prompt.
- [Question/answer ordering is lost in an MCP result] -> Record each server-labelled interaction pair in request order inside the enclosing tool result and assert it in replay, transcript and export tests.

## Migration Plan

This is additive and requires no settings, package, session-schema or existing-session migration. Implement and verify the shared question contract first, then the TUI interaction and non-interactive fallback, then MCP/ACP adapters, and finally transcript/export presentation. Each slice is test-first. Existing approval decisions and session files remain unchanged. No golden session fixture should change; a golden failure is a stop-and-report condition under the repository rule.

## Open Questions

- Maintainer sign-off is required before implementation because this adds a tool contract and protocol behavior. Apply must not begin until the proposal is on `main` and that sign-off is recorded. The implementation proposal may record a new durable ADR only if the maintainer chooses a decision that must persist beyond this change; do not pre-assign or renumber an ADR here.
- Current ACP support has no non-approval user-input request, so its v1 behavior is the immediate unanswered fallback unless a client advertises an eligible mechanism.
