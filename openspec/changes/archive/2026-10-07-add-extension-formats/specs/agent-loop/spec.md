## ADDED Requirements

### Requirement: Nested calls in the loop
The loop SHALL execute nested tool calls (through `ToolContext.ExecuteToolAsync`) on the same ordered path as top-level calls: same approval, same cancellation token, same event stream. Nested results SHALL NOT enter the conversation history — only the outer call's result does. The depth cap (`MaxNestedToolDepth`) and the exposure gate SHALL be enforced by the loop. User cancellation SHALL propagate out of nested calls; every other failure SHALL become an error result.

#### Scenario: Nested work leaves history to the outer call
- **GIVEN** a run where a tool makes nested calls
- **WHEN** the run completes
- **THEN** the history contains the outer call's single result and no nested entries

#### Scenario: Cancellation propagates through nesting
- **GIVEN** a run cancelled while a nested call is in flight
- **WHEN** the cancellation reaches the nested call
- **THEN** the run stops with the cancellation semantics of T-10 and the history stays valid
