# Design — publish the extension contract

The surface is decided (the issue's rulings, ADR-0002 addendum, ADR-0026 condition 5); the remaining
work is packaging mechanics. The facts below come from running the mechanics in scratch before
committing to them (packing every project to a folder feed, unpacking each nupkg, restoring and
compiling a consumer from it).

## The package set

| Package id | Version | Stability | nuspec dependencies | `lib/net10.0` contents |
|---|---|---|---|---|
| `Lunate.Contracts` | 1.15.0 (`ExtensionApi.Current`) | stable | `Microsoft.Extensions.AI.Abstractions` | its own assembly |
| `Lunate.Extensibility.Abstractions` | 1.15.0 | stable | `Lunate.Contracts` 1.15.0, MEAI.Abstractions | its own assembly |
| `Lunate.Agent` | 0.2.0-preview.1 | preview | `Lunate.Contracts` 1.15.0, MEAI.Abstractions | its own assembly + `Lunate.Ai.dll` |
| `Lunate.Extensibility.Testing` | 0.2.0-preview.1 | preview | `Lunate.Agent` 0.2.0-preview.1, `Lunate.Extensibility.Abstractions` 1.15.0 | its own assembly + the harness closure (below) |

## Why the preview packages are self-contained

Scratch experiment: `dotnet pack` of `Lunate.Agent` as-is emitted a nuspec dependency on
**`Lunate.Ai`** — NuGet names every project reference as a dependency, and `Lunate.Ai` is not a
published package. The result would push cleanly and then fail every install with NU1101; likewise
`Testing` would name `Lunate.Extensibility` and the tool. Publishing `Lunate.Ai`, `Extensibility`
and more is real scope growth the card does not license, so the dependencies instead stop being
dependencies:

- each project reference whose target is not a published package carries
  `PrivateAssets="All"` (tested: this suppresses the derived dependency and keeps the reference
  compiling and running in the solution as before);
- a `PackOwnAssemblies` target copies the unreferenced-by-packages assemblies into `lib/net10.0`
  explicitly, by file name, so the package's `lib` is self-contained and readable; package-supplied
  assemblies (`Contracts`, `Abstractions`, `Agent`) are never in the list — a consumer gets them
  once, through the dependency chain, which is also the identity plan (the extension bindings and
  the kits all compile against the host-owned copies).

For the Microsoft compiler packages, nothing is decided here: `Lunate.Roslyn.BuiltIn` and `Lunate.Roslyn`
ride in the Testing kit's `lib`, but the Microsoft compiler packages behind them do not. A consumer
that registers the roslyn built-in in a test host resolves them through its own references;
documenting that in the kit README is the preview's honest state.

Scratch proof: packed the set to a folder feed; restored and built the template's **test** project
against `Lunate.Extensibility.Testing` + the two stable contract packages — restore and compile
succeed, the kit's whole public surface compiles from the feed (the test run itself was not
exercised in scratch; the gate runs the template's tests in-repo as it does today).

## Open questions this issue left to the design step

**Does `Abstractions` version independently of the tool below 1.0?** No — it cannot drift. The
two stable packages carry `ExtensionApi.Current` exactly (`1.15.0` now), in lockstep with each
other per the ADR-0002 addendum, and independently of the tool's version (`0.1.0`, which moves at
release time on its own). A test pins the parity (csproj versions ↔ `ExtensionApi.Current`) so the
bump procedure is one edit everywhere the change updates. Badges of this split are already in the
tree: the tool has never been 1.15.0.

**What is the deprecation policy for an `ExtensionApi` bump?** As little surface movement as the
gate can referee. Within a major version, the contract only grows: a minor bump adds surface and
the published stable package stays installeable, while an extension built against the previous
minor keeps loading — the manifest's `^1.x` range tracks the major, and the gate refuses only a
floor above the host (`Refuse`/`RefuseRange`, load-time, with the required version named before any
extension code runs). Removing or reshaping surface happens only at the next **major** bump,
together with the `Lunate.Agent`-location type forwards (they cost nothing until then, per the
ADR-0002 addendum). "Deprecated" is therefore not a runtime state: it is the changelog naming the
surface the next major drops, and the gate messages are the promise that the drop cannot be silent.
This wording goes in the stable packages' README.

## The template's two shapes

The acceptance is "the template installs `Abstractions` from NuGet", but the packages do not exist
on nuget.org until the next tag publishes them, and a template that cannot restore would break the
gate the moment this PR merges. The template's csproj therefore picks by what exists:

```xml
<ProjectReference Condition="Exists('../../../src/…')" … />
<PackageReference Condition="!Exists(…)" Include="Lunate.Extensibility.Abstractions" Version="1.15.0" … />
```

- in-repo builds keep the project references (source exists) and the gate keeps building the
  template as it does today;
- a copy outside this repository gets the package references — that is the shape the acceptance
  means, and the attribute set matches (`PrivateAssets="all"`, `ExcludeAssets="runtime"`);
- `verify.sh` additionally builds the test-template copy with NuGet references forced against a
  folder feed of the freshly packed packages, so the package path is proven on every commit and
  does not wait for a tag.

The docs snippet (`docs/extensions/README.md`) regenerates from the template file through
`scripts/docs-sync`, so the page moves with the template; the "not published as a NuGet package in
this version" sentence is replaced by what is true after this change.

## The extension package roundtrip

`check-package-metadata.sh` gains icon checks (declared + packed at root, same fixture style as the
readme checks) and a `--prerelease` flag (requires the version name to carry `semver` prerelease
text). The release workflow packs all seven packages (tool, reference bundle, four SDK), checks each
one's metadata, and pushes them in the same push step — `dist/*.nupkg`, OIDC login unchanged. The
workflow's reference-extension path is fixed: `samples/extensions/subagents/…` does not exist, the
bundle lives at `extensions/subagents/…`, which is the path `scripts/verify.sh` already packs.

Preview versions: `0.2.0-preview.1` for the first publish. Increments of `<n>` happen per republish
of a re-packaged preview (NuGet refuses an id+version duplicate; `--skip-duplicate` already guards
the push), and they are decoupled from `ExtensionApi` — a preview version carry no contract
promise... (Wording carried into the Agent/Testing READMEs.)

## Deterministic / SourceLink

`Directory.Build.props` already sets `Deterministic=true` and
`ContinuousIntegrationBuild` on CI, and the release build is CI. Repository URL + RepositoryType on
each packable project let the SDK's built-in Git Source Link embed source-repository metadata into
the PDBs on the release build — no new package, per the no-new-NuGet rule. Recorded here because
#237's metadata list names it; nothing to add.
