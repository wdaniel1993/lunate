## MODIFIED Requirements

### Requirement: Enforced layering
Project references SHALL point only downward in the architecture: `Lunate.Ai` ← `Lunate.Agent` ← `Lunate.Protocols` / `Lunate.Coding`, and `Lunate.Tui` SHALL reference no other Lunate project. `Lunate.Agent` SHALL reference, at runtime, only `Microsoft.Extensions.AI.Abstractions` and the BCL in addition to `Lunate.Ai`; build-only packages with `PrivateAssets="all"` are exempt. The architecture test SHALL enforce both the project-reference and the package rules.

#### Scenario: Upward reference is rejected
- **GIVEN** a project graph containing an upward reference, e.g. `Lunate.Ai` → `Lunate.Agent`
- **WHEN** the architecture test runs
- **THEN** it fails and names the offending reference

#### Scenario: The real graph passes
- **GIVEN** the repository's actual project graph
- **WHEN** the architecture test runs
- **THEN** it passes

#### Scenario: A runtime package outside the allowed set fails the architecture test
- **GIVEN** a runtime package reference added to `Lunate.Agent` (no `PrivateAssets="all"`) outside the allowed set
- **WHEN** the architecture test runs
- **THEN** it fails and names the offending package

### Requirement: Public API visibility
Library projects SHALL track their public surface in `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`, and verification SHALL fail when a `Shipped` file changes relative to the merge base with `main` (or the branch base where a remote is unavailable).

#### Scenario: Shipped API change is caught
- **GIVEN** a committed or uncommitted modification to any `PublicAPI.Shipped.txt` on a branch
- **WHEN** `scripts/verify.sh` runs
- **THEN** it exits non-zero and shows the diff relative to the merge base with `main`
