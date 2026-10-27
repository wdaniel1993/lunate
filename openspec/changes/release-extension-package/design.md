# Design: one sample, packed as a bundle, published on the same tag

## D1 — Which sample, and under which id

The package has to be one a user would actually install, otherwise the smoke proves a fixture's
path and the id squats a namespace for nothing. `samples/extensions/subagents` is the reference
extension: it is the subagent tool the guide describes, it references only the contract,
`Lunate.Agent` and `Lunate.Ai` (so the bundle ships no host assembly), and it is the first thing a
new user asks for.

Decision: **`Lunate.Samples.Subagents`**, published under `wdaniel1993` — the account that already
owns `lunate` and the Trusted Publishing policy this repository's `release.yml` uses.

Alternatives, and why not:

- `Lunate.Extensions.Subagents` — reads as a first-party feature, which the extension is not; the
  word *sample* is the honest one and keeps the support promise small.
- `lunate.subagents` — too close to the tool's own id while being a different product; a user
  reading `lunate install lunate.subagents` cannot tell whether that is the agent or an extension.
- A throwaway `Lunate.Smoke.Fixture` — proves the plumbing and gives users nothing; it also
  spends a permanent id on a test.

The id lives in exactly one place (`PackageId` in the sample's project) plus the smoke's
`LUNATE_SAMPLE_PACKAGE` and the docs; a different decision is a rename, not a redesign.

## D2 — The bundle is the build output, packed after it

`dotnet pack` writes `lib/<tfm>/` by default; a bundle is `lunate.package.json` at the root and the
extension under `extension/` (ADR 0017 addendum, P1). `IncludeBuildOutput=false` plus a
`TargetsForTfmSpecificContentInPackage` target collects `$(TargetDir)` after `Build` and stages it
into a directory that is removed first — so a file a previous build left in `bin/` cannot ride into
a package — and declares it at `extension/`.

Two pack warnings are suppressed with the reason stated in the project: NU5100 (an assembly outside
`lib/`) and NU5128 (no dependency group). Both describe the shape a bundle is supposed to have; a
package a project references would need `lib/`, and this one is installed by `lunate install`.

The package version is the repository version (`Directory.Build.props`), so a tag publishes the
package at exactly the version the tag names. The extension's own `extension.json` version stays
`0.1.0` — it is the extension's version, not the package's, and the record prints both.

## D3 — Published from the same job that publishes the tool

The `pack` job already holds the Trusted Publishing exchange (`NuGet/login@v1` behind the
`production` environment). The sample is packed into the same `dist/` and pushed by the same
`dotnet nuget push dist/*.nupkg`, so both packages are published on the tag with one credential and
one failure mode; a dry run (`workflow_dispatch`) still pushes nothing. `--skip-duplicate` keeps a
re-run safe.

Attaching the package to the GitHub release as well is nearly free (the `release-nupkg` artifact is
already downloaded and uploaded) and gives an offline path: download it and point a `nuget.config`
at the folder. `SHA256SUMS` now covers every attached asset, not only the archives.

## D4 — The smoke installs by id, in a throwaway store

The smoke runs against a published tag, so its install is the user's command: `lunate install
<id>@<version>` against whatever feed the runner's NuGet configuration names (nuget.org on a clean
runner), then `lunate packages --verify`, `lunate extensions` (the installed extension must be
discovered and pending trust) and `lunate uninstall`, asserting that neither content nor record
survives.

- The version is pinned because a bare install resolves the latest *stable* version and a
  prerelease tag publishes none; a tag without a hyphen additionally runs the bare form in a
  second store, because that is the documented command and its resolution is only real against a
  real feed.
- The state root is `$RUNNER_TEMP/lunate-home`, so a maintainer's store can neither make the run
  pass nor be written by it.
- Freshly pushed packages are briefly invisible while the feed indexes them; the install retries
  with backoff (five attempts, ~2 minutes) before it fails, so a slow index is not a red release.

## D5 — The gate proves the artifact on every commit

The smoke runs once per tag; the bundle layout is new, so a regression would otherwise surface at
the worst moment. `scripts/verify.sh` and `scripts/verify.ps1` pack the sample and run the same
install/verify/uninstall cycle from a folder feed with the *published* binary, on all three
operating systems. The feed is a folder, not nuget.org: the gate stays network-free, and the smoke
keeps the only proof of the feed path.
