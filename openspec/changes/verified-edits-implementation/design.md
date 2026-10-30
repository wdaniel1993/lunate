## Context

The approved design is in [`../verified-edits/design.md`](../verified-edits/design.md) and
[`adr/0025-verified-edits.md`](../../../../adr/0025-verified-edits.md). ADR-0017's 2026-10-30
addendum places verification behind a public extension seam: the write tools must not call
`ICSharpBackend` or acquire a Roslyn dependency. Existing extension registration is manifest-driven,
the contract assembly is `Lunate.Extensibility.Abstractions`, writes pass through the host's
approval and mutation ordering, and per-run C# workspaces are lazy per ADR-0021.

The implementation spans the extension contract, host, Roslyn verifier, settings, and TUI. It adds
no package and does not change session format. Source generators run in the in-memory compilation;
only the workspace-loaded TFM and diagnostics mapped to the edited file are checked, not dependent
projects.

## Goals / Non-Goals

**Goals:**

- Let an extension register a read-only verifier for manifest-declared filename patterns.
- Call it after approval and between read/normalize and apply/write, under a 10 s ceiling.
- Implement the C# verifier in `Lunate.Roslyn`, with `block`, `warn`, and `off` settings.
- Preserve the two-refusal escape hatch, show live progress, and make Esc cancel without writing.
- Add tests first, pin the calibrated budget, and update claims only after behavior is real.

**Non-Goals:**

- A core-to-Roslyn reference, an extension tool, or a `ToolCalling` hook.
- Workspace preloading from a `.cs` read/edit or at startup. ADR-0021 says “Nothing loads before
  the first `cs_*` call.” A `.cs` read/edit is not a `cs_*` call, so starting the workspace load
  from either is forbidden as ADR-0021 stands; it is left out and would need an ADR-0021 amendment,
  which the maintainer gives.
- TypeScript verification (tracked separately by #340), dependent-project checks, analyzer-error
  gating, or a new NuGet dependency.

## Decisions

### Public contract and manifest shape

Add `IWriteVerifier`, `WriteVerifierRegistration`, `WriteVerificationRequest`,
`WriteVerificationResult`, and `WriteVerificationStatus` to
`Lunate.Extensibility.Abstractions`. `IExtensionContext.RegisterWriteVerifier` registers a unique
name and verifier instance. `IWriteVerifier.VerifyAsync(request, cancellationToken)` receives an
immutable request containing the normalized workspace-relative path, baseline text, staged text,
and workspace identity. It receives no file handle, filesystem service, or write callback.

The result status is exactly `Verified`, `Unverified`, or `Refused`; it carries an optional reason
and optional `StructuredContent` diagnostics. It carries no replacement content. The host owns the
staged content and is the only writer. This is an API capability boundary, not a claim that a
trusted in-process extension is OS-sandboxed; ADR-0017's trust model is unchanged.

Add optional `writeVerifiers` to `extension.json`, with entries shaped as
`{"name":"csharp","patterns":["*.cs"]}`. Names are unique per manifest and registration. Patterns
are non-empty filename globs without path separators, matched case-insensitively against the
normalized basename at any workspace depth. A registered-but-undeclared name refuses activation;
a declared-but-unregistered name is logged and omitted, following the existing service
cross-check. Bump the extension API to the next minor version (`1.13.0`) and track all new public
members in `PublicAPI.Unshipped.txt`.

### Host invocation and outcome policy

The write/edit host path first completes approval and computes the exact normalized staged text
inside the existing per-path mutation serialization. It then selects a verifier by manifest pattern
and invokes it before applying bytes. `verify: off` bypasses selection and invocation. The host
bounds the call with a timeout token and a 10 s `WaitAsync` ceiling: timeout becomes unverified with
a named timeout reason and the host writes the original staged content. Run cancellation (including
Esc) is distinct from timeout: propagate cancellation and do not write.

In `block`, `Refused` leaves the file unchanged and returns an error with diagnostic
`StructuredContent`; the text lists introduced errors but not the escape hatch. In `warn`, a refusal
writes the original staged content and reports unverified with the verifier diagnostics. A
successful `Verified` result stays verified in either mode. `Unverified` always writes and reports
its reason. Count refusals by the mutation queue's canonical file identity per run; after two
refusals, the third attempt writes unverified with a notice. This notice may explain the current
write's unverified status; refusal messages never advertise the bypass.

The C# verifier is registered by the built-in surface supplied from `Lunate.Roslyn`; it alone uses
the Roslyn backend. It uses an already-loaded per-workspace compilation and does not initiate a
load. With no workspace loaded by an explicit `cs_*` call, it returns unverified with
`workspace-not-loaded`. For a loaded project, replace the edited document's text in memory, run
source generators, compare baseline and staged compiler errors for the staged file, and filter
dependents out. Use the workspace's selected TFM; do not iterate other target frameworks. Compare
errors as multisets keyed by exact `(code, message)` counts, ignoring positions; refuse only keys
whose count increased. Warnings and analyzer diagnostics never refuse.

### Settings, progress, and budget

Persist top-level `verify` in `~/.lunate/settings.json`; accepted values are `block`, `warn`, and
`off`, default `block`. No environment-variable override is added. This keeps the setting consistent
with the user-requested key while avoiding an unrequested second configuration surface.

During the bounded call, expose a verification activity through the existing run event/live-area
path. The TUI renders its activity line as `verifying… <elapsed>` (with the normal spinner and
elapsed seconds); the existing Escape binding cancels the run, and cancellation before apply means
no bytes are written. The call ceiling is 10 s, recalibrated against warm measurements under
ADR-0005 and pinned in the performance suite. A timeout names its reason and never degrades
silently.

### Component and flow view (lightweight C4-inspired ASCII)

```text
┌────────────────────── Lunate.Coding host ──────────────────────┐
│ approve → read/normalize → select pattern → bounded call       │
│                                        │                        │
│                                        ▼                        │
│                              IWriteVerifier                     │
│                                        │                        │
│                         verified / unverified / refused         │
│                                        ▼                        │
│                              host applies staged text           │
└───────────────────────┬────────────────────────────────────────┘
                        │ public contract only
                        ▼
┌──────────── Lunate.Extensibility.Abstractions ──────────────────┐
│ manifest declaration + registration + immutable request/result  │
└───────────────────────▲────────────────────────────────────────┘
                        │ implements, no write API
             ┌──────────┴──────────┐
             │ Lunate.Roslyn C# verifier │  (already-loaded workspace)
             └──────────────────────────┘
```

**Boundaries and assumptions:** core tools know only the public contract; Roslyn is reached only by
the registered verifier; only the host commits staged bytes. A verifier timeout does not cancel the
write, but user cancellation does. An unloaded workspace is an explicit unverified outcome, not a
reason to prewarm or violate ADR-0021.

### Decisions taken

- `verify` is a top-level settings-file field with no environment override.
- Verifier globs match basenames, case-insensitively, at any directory depth; path-specific glob
  syntax is not introduced.
- The public contract advances to `1.13.0`, the next minor after the current `1.12.0` contract.

## Risks / Trade-offs

- [An extension ignores cancellation] -> `WaitAsync` bounds host latency; the contract grants no
  file-write API, and the host never accepts replacement content.
- [A cold session's first edit is not verified] -> return unverified with `workspace-not-loaded`;
  explicit `cs_*` use loads the workspace, in accordance with ADR-0021.
- [Only one TFM and the edited file are checked] -> name both exclusions in the result/docs and
  preserve dependent diagnostics through the existing `cs_diagnostics` path.
- [The 10 s value drifts from real workloads] -> recalibrate from warm measurements under ADR-0005
  and pin the resulting ceiling in the performance suite.

## Migration Plan

This is additive. Ship the contract and manifest parser first, then host invocation, then the
`Lunate.Roslyn` registration, settings and TUI. Existing extensions omit `writeVerifiers` and are
unchanged. The default `block` applies only when a matching verifier exists; no matching verifier
preserves current write behavior. If a regression requires rollback, remove the built-in C# verifier
registration and default the setting to `off` in a follow-up; retain the public API and manifest
parser for compatibility.

## Open Questions

None. The TypeScript verifier remains #340; the workspace-loading boundary is fixed by ADR-0021 and
the maintainer-approved design.
