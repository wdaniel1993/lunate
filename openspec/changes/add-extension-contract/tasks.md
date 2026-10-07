# Tasks

## 1. Contract assembly (TDD, red-first)

- [ ] 1.1 New project `src/Lunate.Extensibility.Abstractions/` (net10.0, nullable, warnings-as-errors, PublicAPI analyzer + `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`); references `Microsoft.Extensions.AI.Abstractions` only; added to the solution and to the PublicAPI gate list in `scripts/verify.sh`
- [ ] 1.2 Contract types per design.md: `IExtensionFactory`, `IExtension`, `IExtensionContext` (id, settings, secrets, log), `IExtensionSettings`, `IExtensionSecrets`, `IExtensionLog`, `ExtensionManifest`, `ExtensionApi` (current version const + range matcher)
- [ ] 1.3 Manifest parsing with actionable errors naming the file and field; unknown fields preserved/ignored per design; duplicate declarations rejected
- [ ] 1.4 API-version range matcher: exact and caret grammar per design; incompatible ranges refused with a clear message; tests for accept/refuse boundaries

## 2. Host loader

- [ ] 2.1 New project `src/Lunate.Extensibility/` (+ solution); discovery of global and project extension directories; manifests read at startup, assemblies not loaded; missing directories are fine
- [ ] 2.2 `ExtensionLoadContext` (collectible): abstractions, MEAI abstractions and System.Text.Json resolve from the default context; all other dependencies load from the extension directory; missing dependency yields an actionable error
- [ ] 2.3 `ExtensionLoader`: `Discover`, lazy `Load(id)` (factory `Create`), `Unload(id)`; duplicate ids across scopes are an error naming both paths
- [ ] 2.4 Test fixture extension: a minimal classlib (`tests/TestExtensions/HelloExtension/`) referencing the abstractions, built with the solution, copied with a manifest into temp extension directories by the tests; real load, real `Create`, real unload (a second load after unload works)

## 3. Settings, secrets, trust

- [ ] 3.1 Settings store: `~/.lunate/extensions-settings/<id>.json` read + validated against the documented schema subset (top-level `required` + `type`; `properties` one level); violations name the field; `IExtensionSettings.Get(path)` (dot-separated)
- [ ] 3.2 Secrets store: `~/.lunate/extensions-secrets/<id>.json`, namespaced, read-only API (`TryGet`); never surfaced in logs or session files (test: a session written during a loaded extension contains none of the secret values)
- [ ] 3.3 Trust: `~/.lunate/trust.json` keyed by repository identity (host-provided string, `GitCommonDir` per ADR-0017); project extensions require approval matching the current content hash (SHA-256 over sorted relative paths + bytes); re-prompt on change; `IExtensionTrustPrompt` (host callback); global extensions are not gated; tests with fake prompt (approve/deny/hash change)

## 4. Budget and close

- [ ] 4.1 Startup budget test: discovering 10 installed extensions (manifests only) under an explicit CI tripwire; report the measured value in the test output
- [ ] 4.2 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-extension-contract --type change --strict`; self-review; commit per group
