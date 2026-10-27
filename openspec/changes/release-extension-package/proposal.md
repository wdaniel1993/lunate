# The release ships an installable extension package

## Why

The release smoke is meant to prove that one extension installs from a package, appears in
`lunate packages`, verifies and uninstalls. It cannot: no extension package exists on any feed.
Every project under `samples/` and `templates/` leaves `IsPackable` at the repository default
(`false`) and sets no package id, so `dotnet pack` produces nothing, and `release.yml` publishes
only the `lunate` tool. The package path is covered by tests with hand-built nupkgs — a real
`dotnet pack` has never produced a bundle, and no published feed has ever served one.

That gap is not only a test-coverage gap. `lunate install <id>` is the documented way to get an
extension that is more than one file, and the reference extension (the subagent tool) is the one a
new user is most likely to want: without a published package, that sentence in the README is a
promise nobody can act on.

## What changes

- `samples/extensions/subagents` becomes packable as `Lunate.Samples.Subagents`: a bundle nupkg
  (`lunate.package.json` at the root, the extension under `extension/`) whose version is the
  repository version, packed from the build output after the build that produced it.
- `release.yml` packs it beside the tool, pushes both through the same Trusted Publishing login,
  and attaches both to the GitHub release; `SHA256SUMS` covers every attached asset.
- `smoke.yml` installs the package by id from the feed a user would use (nuget.org), asserts the
  identity it printed, `lunate packages --verify`, discovery of the installed extension (pending
  trust) and an uninstall that leaves no content and no record — on ubuntu, macos and windows,
  in a throwaway state root. A stable tag additionally runs the bare `lunate install <id>` form
  to prove latest-stable resolution.
- The verify gate packs the same sample and installs it from a folder feed with the published
  binary on every OS, so the bundle layout cannot rot between releases.
- `docs/distribution.md`, `docs/extensions/packages.md`, `README.md`, the sample README and
  `CHANGELOG.md` name the package and the smoke.

## Impact

- One new public package id, `Lunate.Samples.Subagents`, published under the maintainer's nuget.org
  account on every `v*` tag. The id is the one decision this change asks for; `design.md` records
  the alternatives.
- No product code, no public API: the packaging is MSBuild metadata on a sample, and the workflows
  and gate scripts are the only code touched.
- The first tag that carries this change publishes the package. A tag cut before it (`v0.1.0-rc.1`)
  has no package, so the smoke's package step fails by name on that tag — which is the honest
  answer, not a regression.

## Verification

- `dotnet pack samples/extensions/subagents/SubagentsExtension/SubagentsExtension.csproj` produces a
  nupkg whose entries are `lunate.package.json`, `extension/extension.json`, the entry assembly, its
  deps file and its PDB — and no host assembly.
- With the published single-file binary and a folder feed: `install <id>@<version>` → `packages
  --verify` → `extensions` (pending) → `uninstall` leaves no content and no record.
- `scripts/verify.sh` on macOS/Linux and `scripts/verify.ps1` on Windows run the same cycle; the
  release smoke runs it against nuget.org on all three.
