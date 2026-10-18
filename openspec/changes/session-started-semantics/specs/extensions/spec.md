# extensions — delta for session-started-semantics

## MODIFIED Requirements

### Requirement: Hook wiring

Hooks SHALL fire where their producers exist: `SessionStarted`/`SessionEnding` (loader lifecycle; session-started fires once per session, after every extension discovered at session start has loaded, so every registered handler observes it with the full extension set — an extension loaded later does not observe it; started safe to run more than once, ending idempotent), `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before the approval prompt, with the resolved tool's annotations — lowercase kebab wire names in declaration order: `read-only`, `destructive`, `idempotent`, `open-world` — on the payload so policy handlers can decide on them), `ToolResultReady` (composing in order; the payload carries the call's arguments as approved, and the result may replace the output, attach model-facing structured content, and set UI-only details), `TurnEnded`, `RunSettled`, `InputReceived` (the input pipeline: a transformed text proceeds, a consumed input starts and queues nothing and is reported as a notice — before anything starts or queues), `ToolsChanged` (tool-registry composition changes on extension load/unload; empty run id — outside a run), `ModelChanged` (the session model switch via `/model` or the picker; empty run id — outside a run), `ProjectTrust` (global-extension handlers in the trust flow), and `Compacting` (before compaction; the last provided summary wins, and handler failure falls back to the default summarization). Every catalogue hook SHALL have a producer. The harness SHALL NOT depend on the extension system: it exposes no-op seam points, and the host adapter maps to the hook DTOs. During a child run, the harness hook payload family (`IParentRunIdPayload`: run starting, context building, provider stream events, message completion, tool calling, tool results, turn ended, run settled, compacting) SHALL carry the child's run id and the parent run id, so handlers can correlate a run tree.

#### Scenario: ToolCalling runs before approval

- **GIVEN** a ToolCalling handler mutating arguments
- **WHEN** a tool call is approved
- **THEN** the user approves the final (mutated) arguments

#### Scenario: The ToolCalling payload carries the tool's annotations

- **GIVEN** a tool declared with annotations (for example `Destructive`)
- **WHEN** a call to it reaches the ToolCalling hook
- **THEN** the payload lists the tool's annotations, and a policy handler can block the call or let it fall through to the approval prompt on that basis

#### Scenario: A provided summary overrides the default

- **GIVEN** a Compacting handler returning `Provide` with a summary
- **WHEN** compaction runs
- **THEN** the provided summary is recorded instead of the default summarization

#### Scenario: The harness stays extension-agnostic

- **GIVEN** a harness configured without hooks
- **WHEN** it runs
- **THEN** behavior is unchanged

#### Scenario: The ToolResultReady payload carries the call's arguments

- **GIVEN** a ToolResultReady handler
- **WHEN** a tool call completes
- **THEN** the payload carries the call's arguments as approved, and a handler returning structured content sees it on the tool result

#### Scenario: Child-run hook payloads carry both run ids

- **GIVEN** a tool call inside a child (subagent) run
- **WHEN** its ToolCalling hook fires
- **THEN** the payload carries the child's run id and the parent run id

#### Scenario: ModelChanged fires on the session model switch

- **GIVEN** a session with a registered ModelChanged handler
- **WHEN** `/model` switches to a catalog model (or the picker confirms one)
- **THEN** the handler observes the new model id with an empty run id

#### Scenario: Every registered handler observes SessionStarted

- **GIVEN** a session with two extensions that both register SessionStarted handlers, the one loaded first sorting before the other
- **WHEN** the session starts
- **THEN** both handlers run, and the later-loaded extension's handler observes the full extension set (a cross-extension service lookup resolves)

#### Scenario: An extension loaded later does not observe SessionStarted

- **GIVEN** a running session whose SessionStarted has already fired
- **WHEN** another extension is loaded at runtime
- **THEN** its SessionStarted handler does not run
