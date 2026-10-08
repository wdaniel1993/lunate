## MODIFIED Requirements

### Requirement: Hook wiring
Hooks SHALL fire where their producers exist: `SessionStarted`/`SessionEnding` (loader lifecycle; started safe to run more than once, ending idempotent), `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before the approval prompt, with the resolved tool's annotations — lowercase kebab wire names in declaration order: `read-only`, `destructive`, `idempotent`, `open-world` — on the payload so policy handlers can decide on them), `ToolResultReady` (composing in order), `TurnEnded`, `RunSettled`, `ProjectTrust` (global-extension handlers in the trust flow), and `Compacting` (before compaction; the last provided summary wins, and handler failure falls back to the default summarization). Hooks without producers yet (`InputReceived`) SHALL be contract-complete and documented as unwired. The harness SHALL NOT depend on the extension system: it exposes no-op seam points, and the host adapter maps to the hook DTOs.

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
