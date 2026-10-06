## MODIFIED Requirements

### Requirement: Complete function calls at the consumer
The pipeline SHALL present function calls only in complete form: a `StreamAccumulator` above the recorder assembles streamed argument fragments per call id, so consumers never act on partial calls. A fragment is a `FunctionCallContent` whose `Arguments` contains exactly one entry under the reserved `$arguments` key holding a raw JSON fragment (a string, or a `JsonElement` of string kind); fragments merge by call id and are emitted as one parsed call in a synthesized update at stream end. When assembly fails, the synthesized call SHALL keep the concatenated raw fragment text under the reserved `$arguments` key and SHALL set its `Exception`. Recordings SHALL remain raw provider output.

#### Scenario: Fragmented arguments are assembled
- **GIVEN** a stream that delivers one function call's arguments in fragments
- **WHEN** it passes the accumulator
- **THEN** the consumer sees one complete call with the full arguments

#### Scenario: Already-complete calls pass unchanged
- **GIVEN** a stream whose function calls are already complete
- **WHEN** it passes the accumulator
- **THEN** the updates pass through unchanged

#### Scenario: Recordings stay raw
- **GIVEN** recording is enabled
- **WHEN** a fragmented stream is recorded
- **THEN** the fixture contains the raw fragments, and replay through the pipeline still assembles them

#### Scenario: Failed assembly preserves the raw text
- **GIVEN** a stream whose concatenated function-call arguments are not valid JSON
- **WHEN** it passes the accumulator
- **THEN** the emitted call carries the raw fragment text under the reserved `$arguments` key and its `Exception` is set
