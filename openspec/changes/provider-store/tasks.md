# Provider Store Tasks

## 1. Approval checkpoint

- [x] 1.1 Land this proposal on `main`, record maintainer approval of the user-only provider boundary, profile/usage selection, decision method semantics and schema 4; write the next sequential repository ADR (0027 based on this review) superseding ADR-0022 and addressing ADR-0015 before implementation. Do not apply without the proposal and sign-off on `main`.

## 2. Provider store and migration

- [x] 2.1 Test-first in `Lunate.Coding.Tests`: add fixtures proving a legacy stored key creates a provider entry idempotently, preserves explicit custom endpoints, and never copies the secret; prove environment credentials override stored keys in CI-style settings.
- [x] 2.2 Test-first in `Lunate.Ai.Tests` and `Lunate.Coding.Tests`: cover multiple named entries per template kind, all twelve template kinds, credential references (auth key, environment variable, helper), optional headers and `eu`/`us`/`local` metadata; prove secrets and header values never appear in persisted provider settings or list output.
- [x] 2.3 Test-first in `Lunate.Coding.Tests`: prove `lunate auth list` lists provider entries with credential present/absent and bounded `reachable`/`unreachable`/`unknown` status, sends no credential or optional header during its probe, and distinguishes two entries of one kind.

## 3. Profiles and scope validation

- [x] 3.1 Test-first in `Lunate.Ai.Tests`: cover profile-to-provider/model resolution and validation for reasoning level/budget, maximum output, temperature, context-window override, and decision thresholds; prove every unsupported setting is refused by field and profile name before a client call.
- [x] 3.2 Test-first in `Lunate.Coding.Tests`: prove project profiles can reference existing user providers and lower limits, while a project provider with an attacker URL and credential reference is refused by the offending key before secret lookup or network access; also prove project settings cannot raise user limits.
- [x] 3.3 Test-first in `Lunate.Ai.Tests`: prove any user-configured model can serve decision, `apiType` only labels `native` versus `adapter`, and an OpenRouter-shaped single provider entry can back chat and decision profiles without vendor-name matching.

## 4. Usage selection and decision behavior

- [x] 4.1 Test-first in `Lunate.Coding.Tests`: cover `/model` switching chat only, explicit `provider/model` resolution with provider defaults, unique bare model ids, and ambiguous bare ids refused with every candidate named.
- [x] 4.2 Test-first in `Lunate.Agent.Tests`: prove chat, fast, decision, subagents, and compaction usages resolve to their configured profiles and each call's usage/provider/model attribution is emitted exactly once; prove absent subagent assignment defaults to chat while retaining `subagents` attribution.
- [x] 4.3 Test-first in `Lunate.Agent.Tests`: prove malformed decision contracts are retried once then fail naming the model, decisions record only model id, `native`/`adapter` and probability, shadow mode is user-opt-in, and decisions never override deny or exceed the allow envelope.
- [x] 4.4 Test-first in `Lunate.Agent.Tests`: prove no decision profile reports unavailable and stays off without calling the chat profile; prove technical contract failure is not a persistent model-capability verdict.
- [x] 4.5 Test-first in `Lunate.Agent.Tests`: prove compaction uses chat by default even when fast exists, fast compaction occurs only after explicit opt-in, and summaries follow the selected profile while preserving existing tail, image, and replay behavior.

## 5. Session accounting and cost

- [x] 5.1 Test-first in `Lunate.Agent.Tests`: add schema-4 session fixtures and round-trip tests for usage, profile, provider name/kind, resolved model id, token lanes, and decision method (`native`/`adapter`) plus probability; prove schemas 1–3 remain byte-identical and do not gain inferred attribution.
- [ ] 5.2 Add the schema-4 golden fixture(s) only after maintainer approval; prove all existing session goldens remain byte-for-byte unchanged. Stop and report any unrelated golden failure; never rewrite an existing golden to make a test pass.
- [ ] 5.3 Test-first in `Lunate.Coding.Tests` and `Lunate.Tui.Tests`: prove `/cost` reports totals by usage and provider, includes contributing model ids, avoids double counting shared profiles, marks unknown positive token lanes unpriced, and formats values invariantly.

## 6. User surfaces and documentation

- [ ] 6.1 Test-first in `Lunate.Coding.Tests` and `Lunate.Tui.Tests`: prove `/model` exposes chat profiles and qualified model selection without changing other usage assignments, and `/cost` is read-only and makes no model call.
- [ ] 6.2 Update `docs/first-run.md` and the auth/`/model`/cost sections of `docs/guide.md` to describe provider instances, credential references, migration, project restrictions, profile settings, decision degradation, and region as metadata only.

## 7. Verification

- [ ] 7.1 Run all touched test projects with fixture-only provider streams; confirm no test requires a live API and no NuGet package was added.
- [ ] 7.2 Run `PATH="/opt/homebrew/bin:$PATH" /opt/homebrew/bin/openspec validate provider-store --type change --strict`, `dotnet build Lunate.slnx -c Release`, `dotnet csharpier check .`, and the repository docs-profile checks; fix failures before review.
