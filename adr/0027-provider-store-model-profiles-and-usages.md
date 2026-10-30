# ADR-0027: User-scoped provider profiles, usages, and capability facts

- Status: accepted — 2026-10-30 (maintainer amendments for `provider-store`, #253)
- Date: 2026-10-30
- Relates to: ADR-0002 (layering and MEAI model types), ADR-0003 (loop ownership), ADR-0015 (pinned session format), ADR-0022 (model catalog and provider auth)

## Context

Provider instances, model settings, and model selection were spread across presets, auth storage,
catalog entries, and one active model. The `provider-store` change introduces named user-owned
provider instances, profiles, and assignments for existing model usages. The change also adds
capability facts to catalog/profile resolution and adds usage attribution to the pinned session
format. ADR-0022's preset-instance and API-key-only provider model no longer describes the accepted
design; ADR-0015 requires any persisted session-format extension to be explicit and versioned.

## Decision

1. **Provider definitions are user-scoped instances.** Built-in provider kinds remain templates for
   adapter selection and defaults. User settings may define multiple named instances of one kind,
   endpoints, and credential references. Project settings may refer to existing user providers and
   profiles or tighten limits, but may not define providers, endpoints, or credential references.
   Region remains metadata and does not route requests.
2. **Core resolves existing usages through profiles and providers.** Selection is usage → profile →
   provider; individual request routing remains inside the declared provider. This change adds no
   new usages. `/model` changes chat only, and compaction uses chat unless the user explicitly opts
   into fast.
3. **Capabilities are extensible facts, not a closed product policy.** API names, input and output
   modalities, and features are open string sets. Facts layer from catalog to provider discovery to
   user profile override; an unknown category remains allowed. The host checks only declared
   requirements of existing usages: chat requires text input and tools, decision requires structured
   output, and an attached image is rejected before sending only when image input is known to be
   unsupported. Unknown image support is attempted; a provider rejection is translated to a
   model-named switch-profile-or-remove-image notice. Image generation, embeddings, and vision
   fallback remain out of scope for #253 and belong to #357.
4. **Decision suitability belongs to the user.** Any model can decide if the user configures it; the
   contract is checked, the choice is the user's, and decisions can only tighten approvals. No
   per-model calibration state, inferred “cannot decide” verdict, or required logprobs policy exists.
   `apiType` labels `native` versus `adapter`; it does not assess a model's intelligence. A response
   must name a defined option and provide well-formed probabilities. A malformed response is retried
   once, then reported as a failed decision naming the model. Shadow mode is opt-in. A decision
   cannot override deny or approve outside the user's allow envelope.
5. **Session schema 4 remains as approved.** Schema-4 attribution records usage, profile, provider,
   and resolved model identity, plus decision method and probability where applicable; it does not
   record capabilities or provider configuration. Schemas 1–3 retain their existing interpretation.

This supersedes ADR-0022's provider-instance and auth assumptions without rewriting that historical
record, and records the schema-4 extension required by ADR-0015.

## Consequences

- Users can name and reuse providers and profiles without making project configuration an authority
  to redirect credentials.
- Capability checks catch known transport incompatibilities while unknown facts remain permissive;
  they do not claim that a configured model is intelligent enough for a decision.
- Decision contract errors remain transient request failures rather than persistent model verdicts,
  and approval policy remains authoritative over every model output.
- Schema 4 remains the attribution boundary. Capability facts are runtime/configuration data and are
  not copied into session files.
- Image generation, embeddings, and vision fallback require the separately scoped #357 work.
