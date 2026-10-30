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

**Profiles select models; capability belongs to the model.** A profile binds a name to one provider
and catalog/discovery model, with supported settings validated before client construction. A
provider instance may back both chat and decision profiles. Decision capability is resolved from
the selected model's metadata (API type `chat` or `decision`) and structured-output metadata, never
from provider kind or vendor-name matching. A decision profile that lacks model capability is
refused with `This model cannot decide` and names the model. The explicit
`adapter:<provider>/<model>` route remains opt-in and uncalibrated.

**Usage is explicit.** The `chat`, `fast`, `decision`, `subagents` and `compaction` usages select
profiles. `/model` changes chat only. Compaction defaults to chat; fast compaction is explicit.
With no decision profile the decision layer remains unavailable and off. A removed or renamed
profile does not invalidate a resumed session: historical attribution remains as recorded, a
one-line notice names the missing profile, and subsequent turns resolve the current chat usage.

**Sessions persist attribution, not configuration.** Schema 4 records only model-call attribution:
usage, profile name, provider name and kind, resolved model id, and for decisions `native` or
`adapter` plus probability. It never records base URLs, headers, credential references or secrets.
The schema bump is needed so provenance and per-usage cost survive a resume. Readers accept schemas
1–4; older schemas retain their original interpretation without inferred attribution. A reader
that encounters a schema newer than it supports refuses at the header with a clear instruction to
update, not a parse error or silent misread.

## Consequences

- A single named provider entry and credential can back chat and decision profiles, including
  OpenRouter-shaped and OpenCode Go access where chat and decisions use different preset endpoints.
- Project configuration cannot redirect a user's credential; project profile references and
  tighter limits remain usable.
- Session exports disclose which usage/profile/provider/model produced work, but not how to reach
  the provider or authenticate to it.
- Schema-1–3 sessions remain readable and preserve their bytes and lack of inferred attribution;
  schema-4 sessions retain per-usage accounting across resume.
- ADR-0022 remains a historical record; its preset-instance and key-only assumptions no longer
  govern provider configuration or profile selection.
