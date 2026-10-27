## 1. The package

- [x] 1.1 `samples/extensions/subagents/SubagentsExtension` is packable as `Lunate.Samples.Subagents`
      (`IsPackable`, `PackageId`, description, MIT, repository, README), versioned by
      `Directory.Build.props`.
- [x] 1.2 The nupkg is a bundle: `lunate.package.json` at the root, the build output under
      `extension/`, staged from a directory that is emptied first, and no host assembly.
- [x] 1.3 `dotnet pack` of the sample succeeds with no warning suppressed that describes a real
      defect (NU5100/NU5128 carry the reason in the project).

## 2. The release

- [x] 2.1 `release.yml` packs the sample into `dist/` beside the tool.
- [x] 2.2 The same Trusted Publishing login pushes both packages; a dry run pushes neither.
- [x] 2.3 The package is attached to the GitHub release, and `SHA256SUMS` covers every attached
      asset (the three archives and both packages).

## 3. The smoke

- [x] 3.1 `smoke.yml` installs `<id>@<version>` from the runner's feed (nuget.org), retrying while a
      freshly pushed package is indexed, and asserts the printed identity.
- [x] 3.2 It asserts `lunate packages --json` lists the package, `lunate packages --verify` exits 0
      with `verified`, and `lunate extensions` discovers the installed extension (pending trust).
- [x] 3.3 It asserts `lunate uninstall` leaves no content and no install record, in a state root
      outside the checkout.
- [x] 3.4 A stable tag also runs the bare `lunate install <id>` form to prove latest-stable
      resolution.
- [x] 3.5 The Windows leg resolves the binary and state root through `cygpath` so the bash steps run
      with a path Git Bash can execute.

## 4. The gate

- [x] 4.1 `scripts/verify.sh` packs the sample and runs install → `packages --verify` → uninstall
      from a folder feed with the published binary.
- [x] 4.2 `scripts/verify.ps1` runs the same cycle on Windows.

## 5. Docs

- [x] 5.1 `docs/distribution.md`: asset contract, asset count, NuGet step, later-releases list and a
      *Release smoke* section.
- [x] 5.2 `docs/extensions/packages.md` and `samples/extensions/subagents/README.md` name the
      published package and its install command.
- [x] 5.3 `README.md` and `CHANGELOG.md` name the package.

## 6. Verification

- [x] 6.1 A real `dotnet pack` nupkg installs, lists, verifies and uninstalls with the built binary
      from a folder feed on macOS (recorded in the PR).
- [x] 6.2 `openspec validate release-extension-package --strict` green.
- [ ] 6.3 `scripts/verify.sh` green on the final head (macOS), and CI green on ubuntu and windows.
- [ ] 6.4 The maintainer approves the package id/namespace; the first tag that carries the change
      publishes the package, and `smoke.yml` runs against that tag on all three operating systems.
