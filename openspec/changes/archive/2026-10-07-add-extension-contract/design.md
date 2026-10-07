# Design: add-extension-contract

## Context

ADR-0017 principles 4 and 6: extensions compile against a small, semver'd contract assembly, never against `Lunate.Agent` internals; in-process extensions are fully trusted and ALC isolates dependencies, not permissions. `docs/spec/extensibility.md` ("Contract and loading") is the detail spec. Card T-36. No new packages; no golden changes; implements an accepted ADR, so no new ADR.

## Projects

- `src/Lunate.Extensibility.Abstractions/` — the contract. net10.0, nullable, warnings-as-errors (repo defaults). References **only** `Microsoft.Extensions.AI.Abstractions`. PublicAPI analyzer + tracking files; added to the solution and to the PublicAPI gate list in `scripts/verify.sh`. Assembly version `1.0.0`.
- `src/Lunate.Extensibility/` — the host loader. References the abstractions only (nothing else from Lunate, so it can host extensions without the agent). Not PublicAPI-tracked yet.
- `tests/Lunate.Extensibility.Tests/` + fixture extension `tests/TestExtensions/HelloExtension/` (classlib referencing the abstractions; a real assembly to load, built with the solution).

## Contract surface (exact shapes)

```csharp
public interface IExtensionFactory { IExtension Create(IExtensionContext context); }
public interface IExtension { }                       // marker; subsystems add capability interfaces over time
public interface IExtensionContext { string Id { get; } IExtensionSettings Settings { get; } IExtensionSecrets Secrets { get; } IExtensionLog Log { get; } }
public interface IExtensionSettings { bool TryGet(string path, out JsonElement value); }   // dot-separated path
public interface IExtensionSecrets { bool TryGet(string name, [NotNullWhen(true)] out string? value); }
public interface IExtensionLog { void Info(string message); void Warn(string message); void Error(string message); }
```

`IExtension` stays a marker: the hook runner (T-37), tool registration (T-38) and services (T-39) extend the context/extension surface as semver-minor additions. `IExtensionLog` is a minimal host sink — no logging package.

## Manifest (`extension.json`)

```json
{ "id": "hello", "version": "0.1.0", "apiVersion": "^1.0.0", "entryAssembly": "HelloExtension.dll",
  "tools": [], "commands": [], "hooks": [], "services": [], "settingsSchema": { }, "capabilities": [] }
```

- `id` required, `[a-z0-9-]+`; `version` required (informational semver); `apiVersion` required (range); `entryAssembly` required (file name inside the extension directory); the four declaration arrays default to empty; `settingsSchema` optional object; `capabilities` informational.
- Unknown top-level fields are **ignored** (forward compatibility). Malformed known fields fail with an error naming the file and the field. Duplicate declarations (same tool/hook name twice) are rejected.
- `ExtensionManifest` is the parsed model (record with the same members).

## API-version range (minimal grammar, no packages)

- Grammar: exact `1.2.3`, or caret `^1.2.3`. Anything else → `InvalidDataException` ("unsupported apiVersion range") naming the manifest.
- Compatibility: exact → current equals it; caret → current ≥ it and same major. `ExtensionApi.Current = "1.0.0"`.
- Incompatible → refusal at discovery with a clear message naming the extension, its range and the current API version.

## Loading

- `ExtensionScope { Global, Project }`; `ExtensionDescriptor(Id, Version, Scope, Directory, Manifest)`.
- `ExtensionLoader.Discover(workingDirectory, repositoryIdentity)` scans `~/.lunate/extensions/` (global) and `<workingDirectory>/.lunate/extensions/` (project), reads and validates manifests only — **assemblies are not loaded**. Missing directories are fine. Invalid manifests are collected into `DiscoveryErrors` (path + message), never fatal. Duplicate ids across scopes are a discovery error naming both paths.
- `Load(id, workingDirectory, repositoryIdentity, prompt, ct)`: trust check first (project only, below); creates a collectible `ExtensionLoadContext`, loads the entry assembly from the extension directory, finds exactly one public `IExtensionFactory` implementation (zero or several → actionable error), builds the context (settings validated, secrets read), calls `Create`, returns `LoadedExtension(Id, IExtension, Descriptor)`.
- `Unload(id)`: drops the instance and unloads the ALC; idempotent; a subsequent `Load` works. State does not survive (documented).
- `ExtensionLoadContext : AssemblyLoadContext(isCollectible: true)`: by **name**, `Lunate.Extensibility.Abstractions`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` resolve from the default context (return null); every other assembly probes the extension directory (`<name>.dll`, `LoadFromAssemblyPath`); a miss returns null (default probing still serves the BCL); a missing dependency surfaces as the loader's actionable error.

## Settings and secrets

- Settings: `~/.lunate/extensions-settings/<id>.json`, read at load, validated against a **documented subset** of JSON Schema: top-level `required` (array of names) and `properties` (`type` in `string | number | integer | boolean | object | array`, checked one level deep). Extra properties allowed; nested schemas are not validated (documented). Violations name the extension id and the field.
- Secrets: `~/.lunate/extensions-secrets/<id>.json` (`{ "name": "value" }`), namespaced per extension, **read-only API** (`TryGet`); no enumeration. Never written to session files or logs. (Writing secrets is a host concern, later; the file may be authored manually.)
- Both stores tolerate missing files (empty settings/secrets).

## Trust (project extensions)

- `~/.lunate/trust.json`: `{ "repositories": { "<identity>": { "trustedAt": "O", "worktrees": { "<worktreePath>": "<hash>" } } } }`.
- Decision is **per repository identity** (host-provided; `GitCommonDir` per ADR-0017): an untrusted repository prompts once via `IExtensionTrustPrompt.ApproveAsync(descriptor, ct)` (host callback; denial → `ExtensionTrustDeniedException`). A trusted repository with a new worktree does **not** re-prompt (hash recorded silently). A changed content hash for a known worktree **re-prompts**.
- Content hash: SHA-256 (lowercase hex) over sorted relative paths + file bytes of every file in the extension directory.
- Global extensions are not gated. Trust records are written atomically (temp file + move).

## Startup budget

- Manifest discovery with 10 installed extensions is a test with an explicit CI tripwire (assert `< 250 ms`; report the measured value). No host integration yet, so the CLI startup budget is untouched; `verify.sh` still guards it.

## Testing strategy

- Contract/manifest/range: unit tests, red-first; boundary cases for the range grammar (exact, caret, major bump, unsupported forms).
- Loader: the fixture extension loads, `Create` runs, unload succeeds and a second load works; missing entry assembly / zero-or-multiple factories / broken dependency → actionable errors; duplicate ids.
- Trust: fake prompt (approve → recorded; deny → refused; hash change → re-prompt; new worktree of trusted repo → silent).
- Settings/secrets: subset validation violations; `TryGet` paths; secrets absent from a session file written during a loaded extension.
- Both cultures (suite runs under de-AT).

## Out of scope

Hook runner (T-37), tool registration (T-38), services (T-39), UI (T-40s), MCP, out-of-process host (T-52), the `/extensions` command, integration into `AgentHarness`.
