## MODIFIED Requirements

### Requirement: ACP approvals, editor file system and resource links

In ACP mode, tool approvals SHALL compose the configured policy with the client's `session/request_permission`: calls the policy already allows SHALL run without prompting; everything else SHALL ask the editor with allow-once, allow-always and reject options — allow-once executes, allow-always executes and is remembered per tool for the session, reject declines as a tool error, and a cancel or request error resolves as a decline. Every client round trip SHALL be bounded: a file request that outlives its timeout SHALL fail as an I/O error, and a permission request that outlives its timeout SHALL decline (both logged) — a stalled editor degrades, it does not hang. A session cancel SHALL also stop an in-flight file round trip: the call fails promptly instead of waiting out its timeout, and the run ends cancelled. A timed-out request that is abandoned SHALL be observed, so a late failure cannot surface as an unobserved exception. A missing file SHALL surface as a not-found failure and other client failures as I/O errors — never masked into one another. Commands SHALL prompt under every policy. When the client advertises the file-system capability, the read, write and edit tools SHALL route file access through the client's `fs/read_text_file`/`fs/write_text_file` so editor buffers are respected (the client owns byte-order marks); without the capability they SHALL use the local disk. Prompt `ResourceLinkContent` blocks SHALL map to model-readable text: a `file://` URI inside the workspace becomes `@<relative path>`, anything else becomes `<name> (<uri>)`, in prompt order.

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
