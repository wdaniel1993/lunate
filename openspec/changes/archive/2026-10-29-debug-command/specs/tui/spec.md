## ADDED Requirements

### Requirement: Diagnostic bundle command

`/debug [path]` SHALL write exactly one text file. Without a path the file SHALL be
`<state root>/diagnostics/<yyyyMMdd-HHmmss-fff>-<session id>.txt`, stamped in UTC, with the state root
`~/.lunate` or `LUNATE_HOME`; the working directory SHALL NOT be the default destination. A path
argument SHALL override the default, a relative path resolving against the session's working
directory; more than one token SHALL be a usage error, and an existing file SHALL never be
overwritten. The command SHALL print the absolute destination it wrote on the next scrollback line,
followed by the notice `may contain paths and configuration; review before sharing`. The file SHALL
be UTF-8 without a BOM with LF line endings, and owner-only on POSIX. The command SHALL be read-only
with respect to the session — no model call and no session write — so it works while a turn runs.
Print mode and ACP SHALL NOT dispatch it as a command.

The bundle SHALL carry these sections:

- version: the `--version` form, the .NET runtime and the platform with its architecture;
- session: the session file path, id, title, recorded entry count, working directory and git branch;
- state root: the root path, whether `LUNATE_HOME` selected it, one line per known path marked
  `present` or `missing`, and the top-level entry names — paths and existence only, with no file
  under the root opened and no contents read;
- config: the resolved settings (configured model, approval policy, tool details, tool output limit,
  startup header, thinking settings, favourites, extra tools, subagent limits, Roslyn limit), the
  stored auth key names without their values, and each custom provider's id, catalog name, model and
  base endpoint;
- model: the resolved model id, provider, whether it came from the catalog or an extension, the
  endpoint, context window, tool and vision support, thinking level, last routed model and wire id
  when present;
- extensions: the `/extensions` rows, every single-file candidate's path, state and reason with its
  trust digest, source digest and lock hash, the locked packages with a `direct` marker, and the
  dependency edges;
- recent events: the last 64 events, oldest first, with `t+` milliseconds since the first listed event
  and `Δ` milliseconds since the line above; each line carries, as they apply, the event type, short
  run, message and call ids, tool name, stop reason, usage counts, result status and a text length —
  never the text, arguments or output.

Every rendered byte SHALL pass `CredentialRedactor` before the file is opened: a credential value
Lunate holds SHALL be replaced by `[redacted:<name>]` by exact match, and the bundle SHALL add no
pattern matching of its own. An endpoint SHALL be printed as scheme, authority and path only, without
userinfo or query. The bundle SHALL never carry the conversation, prompt or reasoning text, tool
arguments, tool output, file or command contents, protocol headers, or a credential the redactor does
not know.

#### Scenario: Default destination is one file outside the workspace

- **GIVEN** an interactive session whose working directory is a repository and whose state root is
  outside it
- **WHEN** `/debug` is submitted
- **THEN** exactly one file exists under `<state root>/diagnostics`, no file was written under the
  working directory, and the output names its absolute path

#### Scenario: The bundle carries the sections a report needs

- **GIVEN** an interactive session with a turn, an extension and a resolved model
- **WHEN** `/debug` is submitted
- **THEN** the file carries the version and runtime, the resolved model and provider, the state root's
  path layout, the extension graph with its digests, the session facts, and the last 64 events with
  their `t+` and `Δ` timings

#### Scenario: The state root lists paths, never contents

- **GIVEN** a state root holding `auth.json` with a stored key
- **WHEN** `/debug` is submitted
- **THEN** the file marks `auth.json` present and contains none of its contents

#### Scenario: A planted credential is redacted before the file is written

- **GIVEN** a fixture that plants a known provider credential and repeats it in a field the bundle
  renders
- **WHEN** `/debug` is submitted
- **THEN** neither the credential value nor the auth file's contents appear in the file, and
  `[redacted:<name>]` does

#### Scenario: Endpoints print without userinfo or query

- **GIVEN** a custom provider whose base endpoint embeds userinfo and a query string
- **WHEN** `/debug` is submitted
- **THEN** the endpoint in the file is scheme, authority and path only, with no userinfo and no query

#### Scenario: The bundle never carries the conversation or tool content

- **GIVEN** a turn with prompt text, reasoning, tool arguments and tool output
- **WHEN** `/debug` is submitted
- **THEN** none of that content appears in the file, and the event lines carry text lengths instead

#### Scenario: An unknown credential is left to the reporter

- **GIVEN** a credential typed into a prompt or title that Lunate does not store
- **WHEN** `/debug` is submitted
- **THEN** the output warns that the file may contain paths and configuration and must be reviewed
  before sharing

#### Scenario: An existing file is not overwritten

- **GIVEN** a path argument naming an existing file
- **WHEN** `/debug <path>` is submitted
- **THEN** the command refuses, names the path, and leaves the file unchanged
