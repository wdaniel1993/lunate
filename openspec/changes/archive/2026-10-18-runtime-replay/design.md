# Design — runtime replay seam

## D1 — Env seam symmetric with recording

`LUNATE_REPLAY` mirrors `LUNATE_RECORD`: a path-valued environment variable read by the factory, not a CLI flag — the harness builds clients through the factory in every mode, so one seam serves interactive, print and ACP alike.

## D2 — Keyless by construction: the check precedes credential resolution

`Create(ModelInfo)` resolves the credential *before* the decorator chain runs (`ResolveApiKey` throws when nothing resolves), so replay must branch before that call: when `LUNATE_REPLAY` is set, the factory returns a pipeline whose inner client is `ReplayChatClient(fixture)` directly — the provider client is never constructed and no credential is read. The recorder/OTel/logging/accumulator wrappers still apply, so a replayed session exercises the same pipeline as a live one (mirroring the tests' `recorderDecorator` seam, which replaces the inner client the same way).

## D3 — Precedence: recording wins

`LUNATE_RECORD=1` + `LUNATE_REPLAY` both set is a contradiction; the recorder wins and the run records (nothing is replayed). Documented in the spec and the architecture note — deterministic and free of a new failure mode.

## D4 — `ReplayChatClient` is unchanged

The client already verifies request digests with actionable mismatch/exhaustion errors and needs no keys or network (spec'd + tested). The seam only makes it reachable from the runtime.

## D5 — Fixtures are setup-specific (noted for card B)

The request digest covers messages, model id and tool names — a replay fixture is only valid for the exact session shape it was recorded in (workspace context included). The demo fixture for card B is recorded once, in its demo workspace, and committed; re-recording is the fix when the demo changes. This is inherent to digest verification, not a seam limitation.

## D6 — Scope

Only the standard resolution path (`Create(model)`) gains the seam. The explicit-credential overload stays untouched: extension-declared providers hand host-read secrets to the host, and replaying those is out of scope.

## D7 — The date pin (`LUNATE_DATE`)

The composed prompt embeds the UTC date, so a replay fixture's request digest would change daily — the demo media would break the day after recording. `LUNATE_DATE` (ISO `yyyy-MM-dd`) replaces the prompt's date when set; precedence: the `now` parameter (tests), then the variable, then the clock. An invalid value fails with an error naming the variable and the format — a silent fallback would surface as a fixture-digest mismatch, pointing the reader at the wrong subsystem. Scope: the prompt's `{date}` token only; logs, recordings and trust timestamps keep the real clock.
