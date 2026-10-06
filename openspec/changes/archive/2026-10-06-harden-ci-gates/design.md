## Context

The review findings this change acts on: the Shipped-API check cannot fail in CI (worktree-vs-index), docs-only pushes to `main` skip lint, the release workflow trusts tags, `verify.ps1` is not a full twin, the layering test ignores packages, and leftover scaffolding remains. All are gate mechanics; none changes product behaviour.

## Goals / Non-Goals

**Goals:** make every promise in the `repo-foundation` and `ci-pipeline` specs actually enforceable in CI; keep each fix small and testable; pin the new semantics in the specs and in the gate self-tests.

**Non-Goals:** reworking the release workflow into the T-32 distribution work (installers, signing); promoting `Unshipped` API entries (future deliberate step); linting `openspec/**` (excluded by config, unchanged); product code.

## Decisions

- **Shipped-API comparison base**: `git merge-base HEAD origin/main`, falling back to `main`, then to `HEAD` (working tree only, with a printed note). On branches, committed and uncommitted Shipped edits are caught; on `main` itself only uncommitted edits are caught, which is correct — main content was already reviewed on its branch. The comparison is scoped to edits and deletions of existing files (`--diff-filter=MD`): adding a new tracking file is the legitimate bootstrap path (Roslyn in this change proves it) and must not trip the gate. The check moves to a `lib.sh` function so `gate-tests.sh` can exercise it in a synthetic repository (edit, deletion, new-file and clean directions, plus the fallback path). CI checkouts that run the gate gain `fetch-depth: 0` so `origin/main` exists on PR runs. Promotion of `Unshipped` entries will intentionally trip this gate one day — that is the forcing function working; the mechanism can be revisited then, with eyes open.
- **Docs lint as its own tiny workflow** (`push` to `main`, filtered to `*.md`, `docs/**`, `adr/**`, `openspec/**`): keeps the 3-OS matrix off docs-only pushes (they cannot break the build) while closing the lint gap. The matrix requirement text is adjusted to say exactly that.
- **Release chain `gate → publish → release`**: the gate job runs `verify.sh` (ubuntu budget 250 ms) and, for tag pushes, checks `v<tag>` against `<Version>` in `Directory.Build.props` before anything publishes; the release job writes `SHA256SUMS` over the archives and uploads it with them. Dry-run dispatches keep producing artifacts without a release, now gated too.
- **SDK pinning**: `setup-dotnet` reads `global-json-file: global.json` in all workflows (installs the pinned 10.0.103; rollForward stays a local-resolution concern). Cache settings unchanged.
- **Package rule scope**: the architecture test enforces `Lunate.Agent`'s runtime packages exactly (`Microsoft.Extensions.AI.Abstractions`); packages with `PrivateAssets="all"` are build tooling and exempt. Implemented in the existing checker/test split so the rule is unit-testable.
- **Roslyn tracking now**: adding the analyzer package and the two `PublicAPI` files to the empty project costs nothing, closes the spec gap today, and makes T-24's surface growth explicit.

## Risks / Trade-offs

- [`fetch-depth: 0` slows checkouts] → small repository; seconds, accepted for a correct check.
- [Merge-base missing in exotic environments] → the fallback chain degrades to the old working-tree behaviour and says so.
- [Release gate adds a job to tag runs] → releases are rare; a red commit can no longer ship.
- [docs-lint and matrix both lint on mixed pushes] → both are cheap; only one runs on docs-only pushes.

## Migration Plan

Not applicable — no state; the new gate rules apply to future pushes.

## Open Questions

- None blocking.
