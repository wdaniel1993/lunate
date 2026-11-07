## Context

rc.3 review finding 1: at rc.3, every text prompt in the setup wizard, `/auth`'s custom row and
`/login` went through `ISecretPrompt.ReadAsync`, the one masked prompt. A user configuring a custom
endpoint saw provider id, endpoint URL, model id, profile name, region and routing descriptions
render as asterisks. Only the credential value must mask.

The prompt chain is: `InteractiveSession` asks through `_secretPrompt` (`ISecretPrompt`), the live
implementation is `TuiSecretPrompt` (a live-area state machine fed by the key pump), the scripted
implementation is the test double in `InteractiveSessionTestSupport`, and `ConsoleSecretPrompt` is
the plain-text fallback. `SecretPromptState` (Lunate.Tui, internal) is the live-state record the
renderer reads.

## Goals / Non-Goals

**Goals:**

- Exactly the credential value ("API key for …" prompts) is masked, in the wizard, `/auth` and
  `/login`.
- Every other typed value echoes: provider id, endpoint URL, model id, profile name, region,
  routing descriptions. Visible fields keep spaces in pasted text; the credential strips all
  whitespace as before.
- Secret plaintext stays out of the live state and every rendered frame.

**Non-Goals:**

- No change to when Enter/Esc cancel, to the credential-reference branches (`env:<VAR>`,
  `lunate auth`-stored key), to the key pump, or to what is written to the stores.
- No new interaction primitives; no session-format change; no public-API change.

## Decisions

- **A visible read beside the masked read, not a parameter on `ReadAsync`.** `ISecretPrompt` gains
  `ReadTextAsync` whose default implementation delegates to `ReadAsync`. The scripted test prompt
  then needs no change (one answer queue, existing suites untouched) and call sites say at a glance
  what they do: `PromptAsync` routes to `ReadTextAsync`, `SecretPromptAsync` (the walk's one masked
  read) routes to `ReadAsync`. Alternative rejected: a bool parameter would force every test double
  and scripted call site to thread a mode argument.
- **`TuiSecretPrompt.ReadCoreAsync(prompt, masked)`** is one state machine; `ReadAsync` and
  `ReadTextAsync` only set the mode. In masked mode a paste strips all whitespace; in visible mode
  it strips only line breaks (carriage return and newline), so pasted URLs with spaces survive and a
  trailing newline is never treated as Enter. Both modes submit on Enter and cancel on Esc. The
  buffer is cleared and the mode reset on completion, so one prompt instance cannot leak a mode.
- **The prompt prepares the display text; `SecretPromptState` carries only that.** The masked read
  posts only `new string('*', length)` — the plaintext of a secret never reaches the live state
  (pinned by the audit and golden frame tests). The visible read posts the typed text, which is not
  a secret. An earlier sketch carried the mode itself on the record (`Masked`), but the renderer
  shows the prepared text as is, so the flag would have been written and never read — dead code; it
  was removed before archive and the record stayed two fields.
- **The wizard's typed credential routes through the masked read now.** At rc.3 the credential step
  called `PromptAsync`, which is exactly the line the visible-read switch made wrong; it now uses
  `SecretPromptAsync`.
- **`ConsoleSecretPrompt` keeps the delegate default.** Its plain-text echo is the same behaviour
  for both modes.

## Risks / Trade-offs

- [A future non-secret prompt is wired to `ReadAsync` again] -> `SecretMaskingAuditTests` walks the
  wizard's custom path, the credential-reference branch, `/auth` custom and preset, and `/login`
  through a recording prompt and asserts `masked == label.StartsWith("API key")`, so a re-wire fails
  the audit.
- [Visible mode stores plaintext in the live state] -> Acceptable: the visible values (endpoint,
  model id, profile name, …) are not credentials, and the audit test pins that the masked prompt
  never carries plaintext.
- [Existing tests depended on masked rendering of scripted prompts] -> The scripted prompt is
  untouched (`ReadTextAsync` defaults to `ReadAsync`), so scripted runs behave exactly as before.

## Migration Plan

Pure behaviour fix inside one release candidate; no configuration, no file format, no migration.
Rollback is reverting the commit.

## Open Questions

None. The rc.3 finding scopes the fix; no in-force ADR constrains the prompt interaction surface.
