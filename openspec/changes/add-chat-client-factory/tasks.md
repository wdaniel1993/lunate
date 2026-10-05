## 1. Scaffolding

- [x] 1.1 Add the Layer 1 packages to `src/Lunate.Ai` (exact pins per design; `Microsoft.Extensions.AI.OpenAI` fetched from nuget.org; stop and ask if a pin needs a version the design did not list)
- [x] 1.2 Embed `models.json` (schema version + a small starter set) as a resource; wire the resource into the csproj

## 2. Factory

- [x] 2.1 `ModelInfo` + `IChatClientFactory` with the fixed pipeline order (telemetry opt-in → logging → recorder slot → provider); stub-testable; no FIC/AIFunctionFactory
- [x] 2.2 Pipeline-order tests (order observable via test doubles) + no-FIC test; PublicAPI.Unshipped.txt updated

## 3. Catalog

- [x] 3.1 `ModelCatalog`: merge embedded + `~/.lunate/models.json` (user overrides by id, duplicate-id error, missing user file tolerated)
- [x] 3.2 Catalog tests (override wins, new id selectable, duplicate error, missing file)

## 4. Close

- [x] 4.1 `scripts/verify.sh` green; reviewer subagent pass; fix what it reports
- [x] 4.2 `openspec validate add-chat-client-factory --type change --strict`; commit per group; update `docs/tasks`-equivalent status if applicable
