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
optional headers whose values are credential references, and region metadata. It contains
references, never resolved credentials or header values. A provider
instance is access to the APIs its preset offers (such as chat and decisions), with each API's
endpoint supplied by the preset. Project settings may reference existing user providers and define
or narrow profiles on them, but may not define providers, endpoints, base URLs or credential
references. Validate that boundary before resolving any secret or making any request.

**Model capabilities are extensible facts, not an allowlist.** Each model descriptor has open string
sets for APIs, input modalities, output modalities and features. A missing set means unknown; a
known set says which members are supported. Facts layer in order from the catalog, provider
discovery and the user's profile override; a later explicit set replaces the earlier set for that
category. New API, modality or feature names require no enum change. The host checks only the
requirements of an existing usage, and an unknown fact is allowed rather than treated as absent.

**Decision selection is the user's judgement; validation is technical.** Any model can serve
`decision` when the user configures it. The host checks the defined options and well-formed
probabilities (one finite value in [0, 1] per option, summing to 1 within contract tolerance). A
malformed response is retried once; if the retry is malformed, the decision fails with the model
named. This transient failure never becomes a persistent verdict about the model. A provider
decision API is labelled `native`; a chat-model path is labelled `adapter`. Every decision records
only model id and this method label. Shadow mode is an optional user setting, not a per-model gate.

**Usage capability checks are scoped to the request.** Chat requires text input and tools when those
capabilities are known; decision requires structured output when known; an image attachment requires
image input when known. Unknown capability is allowed. When a known image limitation applies, the
host tells the user to switch profile or remove the image before sending; when unknown, it sends the
request and translates a provider rejection into that same user-facing message. Fast, subagent and
compaction usages keep their existing behavior. The descriptor supports future APIs and modalities,
but this change adds no new usage.

**Approval is monotonic.** Regardless of model or method, a decision may only tighten the approval
result. It never overrides deny or approves an action outside the user's allow envelope.

**Usage is explicit.** The `chat`, `fast`, `decision`, `subagents` and `compaction` usages select
profiles. `/model` changes chat only. Compaction defaults to chat; fast compaction is explicit.
With no decision profile the decision layer remains unavailable and off. A decision may tighten an
outcome but never override a deny or approve outside the allow envelope. A
removed or renamed profile does not invalidate a resumed session: historical attribution remains as recorded, a
one-line notice names the missing profile, and subsequent turns resolve the current chat usage.

**Sessions persist attribution, not configuration.** Schema 4 records only model-call attribution:
usage, profile name, provider name and kind, resolved model id, and for decisions method
(`native` or `adapter`). It never records base URLs, headers, credential references, capabilities
or secrets.
The schema bump is needed so provenance and per-usage cost survive a resume. Readers accept schemas
1–4; older schemas retain their original interpretation without inferred attribution. A reader
that encounters a schema newer than it supports refuses at the header with a clear instruction to
update, not a parse error or silent misread.

## Consequences

- A single named provider entry and credential can back chat and decision profiles, including
  OpenRouter-shaped and OpenCode Go access where chat and decisions use different preset endpoints.
- Any user-configured model may serve decision; `native` and `adapter` identify the request path,
  not model quality. Malformed contracts retry once and then fail with the model named.
- Capability facts remain open sets and are not persisted in sessions. Unknown facts allow the
  request; only a known usage requirement triggers a technical check.
- Project configuration cannot redirect a user's credential; project profile references and
  tighter limits remain usable.
- Session exports disclose which usage/profile/provider/model produced work, but not how to reach
  the provider or authenticate to it.
- Schema-1–3 sessions remain readable and preserve their bytes and lack of inferred attribution;
  schema-4 sessions retain per-usage accounting across resume.
- Decisions never override a deny or exceed the allow envelope. Shadow mode remains opt-in.
- ADR-0022 remains a historical record; its preset-instance and key-only assumptions no longer
  govern provider configuration or profile selection.
