# Tasks

## 1. Protocol verification (fixtures recorded)

- [x] OpenRouter SystemOne: noul/choice/score, box with an image, unknown type, unknown model — live,
      recorded in `fixtures/`.
- [x] OpenCode SystemOne: noul, image header requirement, 1.14 unavailable — live, recorded.
- [x] Store TypeSafe's published OpenAPI as `fixtures/typesafe-openapi.json` and check the recorded
      fixtures against it in a contract test; a schema hash change fails with the diff.
- [x] TypeSafe direct's live probe is the first implementation task; ship no preset unless a live
      fixture is recorded (defer the preset if the probe cannot be made).

## 2. Project split (before any System One code)

- [x] New `Lunate.Ai.Decisions` (internal): `IDecisionClient` with
      `GetDecisionAsync(request, options, ct)`, `GetService(Type)`/`GetService<T>()`,
      `DelegatingDecisionClient`, `DecisionClientBuilder`, `UseOpenTelemetry()`; depends only on
      `Microsoft.Extensions.AI.Abstractions`.
- [x] The chat-model adapter moves here as `ChatDecisionClient : IDecisionClient` over an `IChatClient`.
- [x] New `Lunate.Ai.SystemOne` (internal): the System One HTTP client `IDecisionClient`, quirk data,
      fixtures; referenced only by composition.
- [x] `Lunate.Agent`'s primitive and validator depend only on `IDecisionClient`; mapping to and from
      `Lunate.Contracts` at the agent boundary.
- [x] `Lunate.Contracts` unchanged for now and references neither project.
- [x] `LayeringChecker` edges and tests: Decisions → MEAI.Abstractions; SystemOne → Decisions;
      Agent → Decisions; nothing else → SystemOne; Contracts → neither.

## 3. Contract 1.16.0 (lockstep)

- [x] `DecisionQuestionKind.Box`; `DecisionAnswer.Box` (`DecisionBox(Top, Bottom, Left, Right,
      Confidence)`); box validation (fractions in [0,1], `top<bottom`, `left<right`, confidence in
      [0,1], wrong-kind rejected).
- [x] `DecisionResult.Confidence` — the computed value (see 5), so extensions see what the router uses.
- [x] Image state validation gated by the model's input modalities, refusal naming the capability.
- [x] `INativeDecisionClient` stays and forwards to `IDecisionClient`, so 1.15.0 consumers keep working.
- [x] Version bump and contract tests at the new version.

## 4. System One client

- [x] `SystemOneDecisionClient`: request mapping per kind (score criteria array, choice/noul records),
      state pass-through (string, object, array, parts), response mapping per kind (noul complement,
      choice probabilities, score legend + expected score, box fractions), `results` JSON for the
      validator.
- [x] Typed `DecisionTransportException` for deterministic 4xx with the provider's message; 422 field
      surfaced; no retry; `max_tokens_exceeded` mapped to "state too large for this model".
- [x] Image-only headers from the preset (`TypeSafe-Danger-Zone-Images` for OpenCode) applied only
      when the request has image parts.
- [x] Request rules: 32 MiB body, 64 nesting levels, finite numbers only, identifiers as strings; gzip
      above 64 KiB; 429/529 backoff with jitter and `retry-after`, at most three attempts.
- [x] `SystemOneChatFacade : IChatClient` — chat calls fail with "model X is a decision model"; its
      `GetService` returns the decision client.
- [x] `HttpClient` with the shared header and credential handlers from #474 slice 2.

## 5. Confidence and routing

- [x] Confidence computed from the distribution with TypeSafe's formulas for noul/choice/score; box
      uses the provider value; recorded in provenance next to the distribution.
- [x] `decisions.routingMinConfidence` (default 0.5): below it the configured chat profile stays; at or
      above it the argmax route is used; transcript records distribution, confidence and outcome.
- [x] Approval mapping documented: `p ≥ t` ⇔ `confidence ≥ 2t − 1`, so existing thresholds are
      unchanged.
- [x] Routing profiles carry a `description` used as the option's criteria; missing description warns
      once; strategy chosen by the catalog entry: one `choice` by default, one `noul` per profile when
      `choiceOrderSensitive` (jev-1.13); provenance names the strategy.
- [x] The noul strategy gates on the winner's margin over the runner-up; a close call keeps the
      configured chat profile.

## 6. Approval gate questions

- [x] Replace the single approve/deny question with four atomic noul questions in one request:
      `writes_outside_workspace`, `network_access`, `irreversible_deletion`, `credential_exposure`.
- [x] State: `tool`, `arguments`, `workspace_root`, `risk_tier`, `untrusted_content`; untrusted content
      never in instructions or criteria.
- [x] Combine in code against one `decisions.approvalsThreshold` in confidence (default `2t − 1` of the
      old probability threshold); any signal at or above it escalates; shadow records every answer.
- [x] Test: each single signal escalates on its own; an adversarial argument string stays data.

## 7. State size and truncation

- [x] Warn when a state is estimated above ~28k tokens (4 chars/token) of the 32k per-question budget;
      say long context is opt-in and slower/less accurate.
- [x] Tool-result consumer sends head and tail with a truncation marker, never a whole output.
- [x] No implicit `TypeSafe-Danger-Zone-Long-Context`; opt-in per provider only.

## 8. Presets, discovery, profiles

- [x] `openrouter` gains `systemone: https://openrouter.ai/api/v1/systemone`.
- [x] `opencode` gains `systemone: https://opencode.ai/inference/systemone/v1/systemone` and the
      image header, documented as Console/Zen (not Go) and billing to the pay-as-you-go balance.
- [x] Catalog entries for `typesafe/jev-1.14` (OpenRouter) and `jev-1.13` (OpenCode); family
      `systemone`; `choiceOrderSensitive: true` on 1.13; no TypeSafe-direct entry in 0.3.
- [x] Discovery via the models endpoint with catalog fallback; decision usage accepts a `systemone`
      profile; chat usage refuses it naming the family.
- [x] Alias profiles refused, naming the alias and the exact version it resolves to.

## 9. Cost

- [x] Record `usage` (`input_tokens`, `output_tokens`, `cost`) from the response, including the
      OpenCode `usage.opencode` block; mark provider-reported.
- [x] `/cost`: decision usage line with model, method label and amount; catalog estimate only when the
      provider omits cost.

## 10. Adapter

- [x] Adapter schema for box (four fractions + confidence) validated by the shared validator.
- [x] Offered only on vision-capable chat models; otherwise "this model cannot decide" names the
      missing capability.
- [x] Adapter prompt teaches atomic questions, structured state, no arithmetic, low confidence means
      "unknown".

## 11. Docs and tool description

- [x] `docs/configuration.md`: `systemone` family, presets, image header, alias rule, cost reporting;
      a Go key on the OpenCode systemone URL bills to pay-as-you-go; OpenCode serves `jev-1.13`
      (no box), OpenRouter `jev-1.14`; confidence and the routing threshold; the 32k-token budget.
- [x] `docs/guide.md`: a decision profile example and the `jev-router` chat example (no code).
- [x] The `decide` tool's description: several atomic questions, named state fields, never splice
      untrusted content, no counting/arithmetic/dates, low confidence = unknown.
- [x] ADR-0030 addendum: MEAI decision abstraction swap path.

## 12. Proof

- [x] Fixture replay per provider (success + error shapes), header-only-with-images, `usage.opencode`.
- [x] OpenAPI contract test over every fixture; schema hash pinned.
- [x] Confidence formulas against the documented examples; native vs adapter equality.
- [x] Routing below/at threshold for both strategies (choice confidence, noul margin); strategy recorded.
- [x] Existing decision-layer tests green with a native profile replacing the adapter.
- [x] Layering graph test and the Contracts isolation test.
- [x] Live smoke: OpenRouter and OpenCode (skip without a key).
- [x] `verify.sh` green and PR CI green.