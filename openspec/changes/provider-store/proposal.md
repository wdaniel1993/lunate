# Provider Store

## Why

Provider credentials, endpoints, model settings, and model selection are currently spread across presets, auth storage, catalog entries, and one active model. This change gives users a secure provider registry and named model profiles, then assigns those profiles to explicit usages so decision, fast, subagent, and compaction calls cannot silently inherit the wrong model.

## What Changes

- Add a user-scoped provider store with preset-derived provider kinds, per-entry endpoints and credential references, optional headers and region metadata; migrate existing stored keys and report provider credential/reachability status through `auth list`.
- Add named model profiles that bind provider + model + validated settings, and usage defaults for `chat`, `fast`, `decision`, subagents, and compaction.
- Resolve `/model`, qualified `provider/model` ids, and bare model ids deterministically; reject ambiguous bare ids with their candidates.
- Require structured-output support for the decision contract; use catalog/discovery API type only to label native versus adapter provenance, allow explicitly assigned chat models as uncalibrated adapters, report missing contract capability separately from invalid decision output, and leave the decision layer off when no decision profile is configured.
- Attribute recorded usage and `/cost` to usage, profile, provider and model; record each decision's method, probability and probability method; use chat for compaction unless the user opts into `fast`.
- Advance the session format to schema 4 for usage/provider attribution, migrate schemas 1–3 on read, and update the schema-4 golden fixtures in the implementation PR after proposal approval.
- Reject project-scope provider definitions, endpoints, and credential references while allowing project settings to reference existing user providers/profiles, define profiles on existing providers, or tighten limits; retain region metadata without implementing data-routing policy.
- Keep routing selection in core at usage → profile → provider; leave per-request routing inside a declared provider such as the `model-router` sample. Add no routing DSL.

## Capabilities

### New Capabilities

- `provider-store`: user provider entries, model profiles, usage selection, decision capability and adapter semantics, cost attribution, project-scope security, migration, and provider listing.

### Modified Capabilities

- `agent-config`: provider/auth storage, key migration, environment precedence, profile settings validation, and project settings restrictions.
- `ai-layer`: capability-bearing model metadata, provider templates versus instances, and profile-setting validation at catalog load.
- `agent-loop`: usage selection for turns, subagents, decisions, and compaction; explicit decision degradation and separated validators.
- `agent-sessions`: durable usage/provider attribution and decision provenance without exposing credential values; schema 4 with backward reading of schemas 1–3.
- `tui`: `/model` selection of chat profiles, provider-list output, and `/cost` breakdown by usage and provider.

## Decisions taken

- Provider definitions live only in user scope; project files may reference or narrow but may not introduce a provider, endpoint, or credential reference.
- Optional request-header values are credential references, not literal values persisted in the provider registry or listed by `auth list`.
- The twelve existing presets are provider templates, not the only permitted provider shape; multiple user entries may share a template kind.
- The decision usage is capability-based, not a vendor allowlist. With no configured decision profile it is unavailable and off, never an implicit chat-model fallback.
- Configuration refuses only an explicit structured-output=false capability when a profile is assigned to decision; unknown capability proceeds to runtime validation. Every adapter response is checked against the defined decision options and well-formed probabilities; after three consecutive contract failures that profile is marked cannot decide for the session and names its model. Catalog/discovery `apiType` labels but never gates. A chat model explicitly assigned to decision is `adapter`, while a provider-calibrated decision API is `native`; these method labels never grant calibration. Every model starts uncalibrated and reaches calibrated only through per-model shadow metrics and maintainer sign-off under #183. Probabilities prefer provider logprobs when available, otherwise use self-reported values, and record their method (`logprobs`, `self-reported`, or `native`). Metrics remain per model id, and decisions may tighten but never override a deny.
- Session schema 4 records the usage and provider for model-call usage and records decision kind/model provenance; schemas 1–3 remain readable. Approved implementation updates the corresponding golden fixtures in the same PR.
- Compaction defaults to the chat profile; `fast` is an explicit opt-in. Region is retained as metadata, with data-routing policy deferred.
- Core selects usage → profile → provider. Provider-internal routing, including the `model-router` pattern, stays inside that provider; no routing DSL is added.

## Impact

- Affects `Lunate.Ai` model/provider metadata and resolution, `Lunate.Coding` settings/auth and command surfaces, `Lunate.Agent` usage selection and decision outcomes, session usage records, and TUI cost/model presentation.
- Updates the user-facing auth and `/model` guidance in `docs/guide.md` and `docs/first-run.md` during implementation.
- No new NuGet package. No secrets are added to provider definitions or session records. Region-based routing policy is out of scope.
