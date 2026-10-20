# Tasks — provider-presets

Each task lands as one commit on `change/provider-presets` after the proposal is on `main`. Tests
are written first. No task adds a package, and no task edits an existing session golden to make it
pass.

## 1. Catalog schema, generator and pricing types

- [x] 1.1 Add `src/Lunate.Ai/providers.json` (schema 1: preset id, display name, adapter kind, base
      URL, key variable, Google alias) and embed it; add `ProviderPreset`/`ProviderPresets` with
      parsing and lookup, and tests for the twelve rows and an unknown id.
- [x] 1.2 Extend `ModelInfo` with init-only `Pricing`, `MaxOutput`, `SupportsVision` and `WireId`;
      add `ModelPricing` and a culture-invariant cost helper; update `ModelCatalog` to schema 2 with
      read-side schema 1 acceptance and the optional managed cache
      (`~/.lunate/models.catalog.json`); update `PublicAPI.Unshipped.txt`.
- [x] 1.3 Add `ModelsDevCatalog` (the one models.dev → catalog mapper) and
      `scripts/update-models.sh` (script version 1.0.0, reads the embedded config, calls
      `--update-catalog --output`, supports a `--check` diff); regenerate
      `src/Lunate.Ai/models.json` (schema 2, source SHA-256, deterministic order, roughly 750
      entries).
- [x] 1.4 Update `tests/Lunate.Ai.Tests/ModelCatalogTests.cs` for the new fields and the schema 1/2
      rules, and add tests for provider config parsing, generated mapping, deterministic output,
      duplicate wire ids, fully/unpriced cost math and the four unchanged bare ids.
- [ ] 1.5 `dotnet csharpier format .`; commit `feat(ai): generate a schema-2 model catalog`.

## 2. Preset routing and credentials

- [ ] 2.1 Route every OpenAI-compatible preset through the existing OpenAI adapter with
      `model.Endpoint ?? preset.BaseUrl`, send `WireId ?? Id`, keep Anthropic native, and make the
      unknown-provider error list the preset ids.
- [ ] 2.2 Resolve credentials from the preset key variable (Google accepts `GEMINI_API_KEY`), then
      the auth store under the provider name; keep the `auth` reference first and the
      placeholder-credential rule for user-declared endpoints. Update `ProviderKeyProbe`.
- [ ] 2.3 Update `DiscoverCommand` for preset targets (preset base URL/key, qualified ids,
      `wireId`) and stamp the draft with the catalog schema constant, not the settings schema; keep
      the direct-URL draft unchanged.
- [ ] 2.4 Update `tests/Lunate.Ai.Tests/ChatClientFactoryTests.cs`,
      `tests/Lunate.Coding.Tests/DiscoverCommandTests.cs` and `ProviderKeyProbeTests.cs`; add
      routing, wire-id, endpoint-precedence and preset-credential tests. Keep `PrintModeTests`
      model-precedence and unknown-model assertions green.
- [ ] 2.5 `dotnet csharpier format .`; commit `feat(ai): route provider presets and credentials`.

## 3. Settings schema 2, favourites storage and auth writes

- [ ] 3.1 Add `favorites` to `AgentSettings` and `SettingsStore` (schema 2, schema 1 read-side,
      ordered, max eight, unique); update `SettingsStoreTests` and
      `SettingsStoreThinkingTests`.
- [ ] 3.2 Add an atomic read-modify-write settings writer that preserves unrelated keys and emits
      schema 2; tests for preservation, validation failure leaving the old file, and POSIX-safe
      replacement.
- [ ] 3.3 Add `AuthStore.Remove`, map every preset to its environment variable, and add the
      `/login <provider>`/`/logout <provider>` backend seam (masked value in, store out; no secret
      in notices); update `AuthStoreTests`.
- [ ] 3.4 Extend `Commands`/`CompletionRegistry` metadata for `/login`, `/logout`, `/favorite` and
      `/unfavorite`; add a secret-input seam the TUI prompt can drive.
- [ ] 3.5 `dotnet csharpier format .`; commit `feat(coding): add favourites and provider login`.

## 4. Explicit catalog refresh

- [ ] 4.1 Add early `lunate --update-catalog [--output <path>]` dispatch: fetch models.dev, map
      through `ModelsDevCatalog`, validate, write atomically, print the count/provider/source-hash
      summary, and leave the user file untouched; update `CliHelp` and `CliTests`.
- [ ] 4.2 Add `tests/Lunate.Coding.Tests/CatalogUpdateTests.cs` with an injected handler: managed
      cache write, `--output` script form, unreachable source, invalid payload, user-file
      preservation and no model call.
- [ ] 4.3 `dotnet csharpier format .`; commit `feat(cli): refresh the model catalog explicitly`.

## 5. Session usage lanes and cost reporting

- [ ] 5.1 Add optional `CacheRead`/`CacheWrite` to `SessionUsage`, write schema 3, accept schema
      1/2/3, and emit the cache fields only when positive; update `PublicAPI.Unshipped.txt`.
- [ ] 5.2 Record the provider's cache counts in `AgentHarness` when it appends usage, without
      changing the token totals or `UsageUpdated`.
- [ ] 5.3 Add the cost aggregator in Coding (per record, per turn, per model, per lane) using the
      catalog model id; wire `/session` to the total + per-model + per-lane report and
      `PrintEventJson` to `costUsd`, omitting all of them when unpriced and keeping a schema 1/2
      session token-only when its model has a cache rate.
- [ ] 5.4 Add a schema 3 golden under `tests/fixtures/sessions/` and a byte round-trip test; keep
      every existing v1/v2 golden byte-identical. Update `InteractiveSessionCommandTests`
      (`/session` with price, cache lanes, one unpriced model, no cost at zero) and
      `PrintEventJsonTests`.
- [ ] 5.5 `dotnet csharpier format .`; commit `feat(agent): persist and report usage cost lanes`.

## 6. TUI: cost, favourites and the masked login prompt (final implementation group)

- [ ] 6.1 Extend `StatusFooterModel` with the favourite position and turn cost; render
      `fav:<n>/<m>` after the model and the invariant `$` segment after usage; update
      `StatusFooterRendererTests`, `PromptFooterGoldenTests`, `FrameGoldenTests` and the narrow-frame
      invariants.
- [ ] 6.2 Add `RoutedKey.FavoritesCycle` for Ctrl+P in `KeyRouter`, update `KeyRouterTests`, and
      wire the Coding session to cycle through settings favourites via `SwitchToModel`, refusing
      while busy/picker/approval/secret prompt.
- [ ] 6.3 Add the masked `LoginPrompt` to the live area (echo off, no history/scrollback/session
      writes, Esc/empty cancels, ordinary input inert) and its scripted key/frame tests.
- [ ] 6.4 Update the `/help` golden, the `Every_built_in_command_dispatches_without_a_model_call`
      matrix, the picker/footer goldens and the command autocomplete rows for `/login`, `/logout`,
      `/favorite`, `/unfavorite`; update `InteractiveSessionPickerTests`, `CommandRegistryTests`,
      `CompletionRegistryTests` and the Ctrl+L/Ctrl+O/Ctrl+P precedence tests.
- [ ] 6.5 `dotnet csharpier format .`; commit `feat(tui): show cost and favourites, add masked login`.

## 7. Documentation, ADR and the gate

- [ ] 7.1 Update `README.md` (quick start, preset table, `/login`, `--update-catalog`, favourites,
      footer cost) and `docs/guide.md` (Layer 1 catalog source and fields, config, session cost, TUI
      footer, key table, slash commands); replace the guide's "remote catalog refresh not planned"
      note with the explicit-refresh behaviour, and document `scripts/update-models.sh` version
      1.0.0 with its embedded `providers.json` config in `CONTRIBUTING.md`. State the
      no-subscription-OAuth reason in one sentence.
- [ ] 7.2 Land `adr/0022-model-catalog-and-provider-auth.md` and keep the change manifest
      `adr.md` pointing at it; note that ADR-0015/0018 are amended by the additive schema 3 usage
      fields and that no prior ADR is edited.
- [ ] 7.3 Run `openspec validate provider-presets --strict` and `scripts/verify.sh` (build, tests,
      single-file publish, performance budgets, PublicAPI, CSharpier, documentation lint); fix
      failures without weakening a check.
- [ ] 7.4 Commit `docs(adr): record the generated catalog and provider auth`.
