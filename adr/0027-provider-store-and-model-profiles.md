# ADR-0027: User-scoped providers, model profiles and usage attribution

- Status: accepted — 2026-10-30 (maintainer sign-off for provider-store, #253)
- Date: 2026-10-30
- Supersedes: ADR-0022 (provider instances and profile selection)
- Addresses: ADR-0015 (schema-4 session extension)
- Relates to: ADR-0002 (MEAI model types), ADR-0003 (agent loop), ADR-0010 (provider adapters)

## Context

The preset/auth model in ADR-0022 cannot represent multiple endpoints and credential sources of
the same kind, named model profiles, or distinct model assignments for chat, fast, decision,
subagents and compaction. The session format in ADR-0015 pins serialized history, so adding
durable usage and provider provenance requires a schema version and explicit older-reader behavior.
Project settings are untrusted and must not gain authority to redirect a user's credentials.

## Decision

**Provider access is user-owned.** The twelve existing presets are templates for adapter defaults
and API endpoints. A named provider instance stores its kind, base URL, credential reference,
optional headers and region metadata. It contains references, never resolved secrets. A provider
instance is access to the APIs its preset offers (such as chat and decisions), with each API's
endpoint supplied by the preset. Project settings may reference existing user providers and define
or narrow profiles on them, but may not define providers, endpoints, base URLs or credential
references. Validate that boundary before resolving any secret or making any request.

**Profiles select models; capability, method and calibration are distinct.** A profile binds a name
to one provider and catalog/discovery model, with supported settings validated before client
construction. A provider instance may back both chat and decision profiles. At configuration time,
refuse assignment to decision only when the catalog explicitly says structured output is absent;
unknown capability proceeds to runtime validation. Catalog/discovery `apiType` labels but never
gates, and vendor names and model ids never imply capability. A provider-calibrated decision API is
labelled `native`; a chat model explicitly assigned to decision is labelled `adapter`, whether used
directly or through the explicit `adapter:<provider>/<model>` wrapper. These method labels do not
grant calibration. Every model starts uncalibrated and reaches calibrated only through per-model
shadow metrics and maintainer sign-off under #183. No model borrows another model's metrics. The
exact distinction is: Capability is about the contract, calibration is about trust, and trust is
earned per model.

Every adapter response is checked against the defined options and probability contract. A
well-formed probability result has one finite value in [0, 1] per option and sums to 1 within the
contract tolerance. Provider logprobs supply probability provenance when exposed; otherwise an
adapter may use self-reported probability, while a native decision API records native probability
provenance. The probability method (`logprobs`, `self-reported`, or `native`) is distinct session
attribution from decision method (`native` or `adapter`). Three consecutive contract failures mark
that profile `cannot decide` for the rest of the session and name the model. The capability refusal
is reserved for an explicit structured-output=false flag and names the model and missing capability;
malformed output after that check remains `invalid decision`.

**Usage is explicit.** The `chat`, `fast`, `decision`, `subagents` and `compaction` usages select
profiles. `/model` changes chat only. Compaction defaults to chat; fast compaction is explicit.
With no decision profile the decision layer remains unavailable and off. A decision may tighten an
outcome but never override a deny, regardless of calibration, under the #183 approval rule. A
removed or renamed profile does not invalidate a resumed session: historical attribution remains as recorded, a
one-line notice names the missing profile, and subsequent turns resolve the current chat usage.

**Sessions persist attribution, not configuration.** Schema 4 records only model-call attribution:
usage, profile name, provider name and kind, resolved model id, and for decisions method
(`native` or `adapter`), probability and probability method. It never records base URLs, headers,
credential references or secrets. The method enum is not calibration state; calibration remains a
separate per-model state.
The schema bump is needed so provenance and per-usage cost survive a resume. Readers accept schemas
1–4; older schemas retain their original interpretation without inferred attribution. A reader
that encounters a schema newer than it supports refuses at the header with a clear instruction to
update, not a parse error or silent misread.

## Consequences

- A single named provider entry and credential can back chat and decision profiles, including
  OpenRouter-shaped and OpenCode Go access where chat and decisions use different preset endpoints.
- A structured-output chat model may be explicitly assigned to decision and is labelled adapter;
  native is reserved as the method label for provider-calibrated decision APIs. Neither method is
  calibrated by default; calibration is earned per model id. Metrics remain separate by model id
  and method.
- Project configuration cannot redirect a user's credential; project profile references and
  tighter limits remain usable.
- Session exports disclose which usage/profile/provider/model produced work, but not how to reach
  the provider or authenticate to it.
- Schema-1–3 sessions remain readable and preserve their bytes and lack of inferred attribution;
  schema-4 sessions retain per-usage accounting across resume.
- Decisions never override a deny, and session-only contract failures mark the offending model
  unavailable without changing persistent configuration.
- ADR-0022 remains a historical record; its preset-instance and key-only assumptions no longer
  govern provider configuration or profile selection.
