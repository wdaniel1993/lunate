# Provider Store

## Why

Provider credentials, endpoints, model settings, and model selection are currently spread across presets, auth storage, catalog entries, and one active model. This change gives users a secure provider registry and named model profiles, then assigns those profiles to explicit usages so decision, fast, subagent, and compaction calls cannot silently inherit the wrong model.

## What Changes

- Add a user-scoped provider store with preset-derived provider kinds, per-entry endpoints and credential references, optional headers and region metadata; migrate existing stored keys and report provider credential/reachability status through `auth list`.
- Add named model profiles that bind provider + model + validated settings, and usage defaults for `chat`, `fast`, `decision`, subagents, and compaction.
- Resolve `/model`, qualified `provider/model` ids, and bare model ids deterministically; reject ambiguous bare ids with their candidates.
- Allow any user-configured model profile for `decision`; validate each response against the decision contract, retry one malformed response once, then fail with the model named. Keep shadow mode opt-in and make every decision tighten approvals without overriding deny or exceeding the allow envelope.
- Add an extensible per-model capability descriptor for API names, input/output modalities and features, layered from catalog to provider discovery to profile overrides; check only known requirements of existing usages and allow unknown capabilities.
- Attribute recorded usage and `/cost` to usage, profile, provider name/kind and model; record decision model id, `native`/`adapter` method and probability; use chat for compaction unless the user opts into `fast`.
- Advance the session format to schema 4 for usage/provider attribution, migrate schemas 1–3 on read, and update the schema-4 golden fixtures in the implementation PR after proposal approval.
- Reject project-scope provider definitions, endpoints, and credential references while allowing project settings to reference existing user providers/profiles, define profiles on existing providers, or tighten limits; retain region metadata without implementing data-routing policy.
- Keep routing selection in core at usage → profile → provider; leave per-request routing inside a declared provider such as the `model-router` sample. Add no routing DSL.

## Capabilities

### New Capabilities

- `provider-store`: user provider entries, model profiles, usage selection, decision contract and method provenance, cost attribution, project-scope security, migration, and provider listing.

### Modified Capabilities

- `agent-config`: provider/auth storage, key migration, environment precedence, profile settings validation, and project settings restrictions.
- `ai-layer`: extensible model capability descriptors, provider templates versus instances, and profile-setting validation at catalog load.
- `agent-loop`: usage selection for turns, subagents, decisions, and compaction; decision contract retry, approval-envelope enforcement, and attachment capability feedback.
- `agent-sessions`: durable usage/provider attribution and decision provenance without exposing credential values; schema 4 with backward reading of schemas 1–3.
- `tui`: `/model` selection of chat profiles, provider-list output, and `/cost` breakdown by usage and provider.

## Decisions taken

- Provider definitions live only in user scope; project files may reference or narrow but may not introduce a provider, endpoint, or credential reference.
- Optional request-header values are credential references, not literal values persisted in the provider registry or listed by `auth list`.
- The twelve existing presets are provider templates, not the only permitted provider shape; multiple user entries may share a template kind.
- Any model may serve `decision` when the user assigns it; capability facts are extensible open sets with unknown allowed, and the host checks only known requirements of existing usages. A malformed contract is retried once and then fails with the model named. Shadow mode is opt-in, not a gate. Decision provenance is its model id, `native`/`adapter` method and probability, in addition to usage attribution. Decisions may only tighten approvals, never override deny or exceed the allow envelope.
- The superseding maintainer amendment removes the previously added per-model calibration state, persistent “cannot decide” verdict, uncalibrated label and probability-source policy; none are part of the design. Session schema 4 records only usage/profile/provider-name-and-kind/model attribution, plus decision method and probability; it never records provider configuration. Schemas 1–3 remain readable. Approved implementation updates the corresponding golden fixtures in the same PR.
- Compaction defaults to the chat profile; `fast` is an explicit opt-in. Region is retained as metadata, with data-routing policy deferred.
- Core selects usage → profile → provider. Provider-internal routing, including the `model-router` pattern, stays inside that provider; no routing DSL is added.

## Impact

- Affects `Lunate.Ai` model/provider metadata and resolution, `Lunate.Coding` settings/auth and command surfaces, `Lunate.Agent` usage selection and decision outcomes, session usage records, and TUI cost/model presentation.
- Updates the user-facing auth and `/model` guidance in `docs/guide.md` and `docs/first-run.md` during implementation.
- No new NuGet package. No secrets are added to provider definitions or session records. Region-based routing policy is out of scope.
