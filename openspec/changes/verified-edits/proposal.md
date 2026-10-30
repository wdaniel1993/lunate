# Verified edits: compile before write, diagnose before apply

## Why

An edit that cannot compile is a failed turn the user pays for twice: once in tokens, once in
undoing it. Lunate already has everything the check needs — the `write`/`edit` tools
(`src/Lunate.Coding/WriteTool.cs:13`, `src/Lunate.Coding/EditTool.cs:14`) and an in-process Roslyn
backend that diagnoses the loaded solution without a build (`src/Lunate.Roslyn/CsDiagnosticsTool.cs`
per ADR-0006/0014) — but the check is not connected: `write` and `edit` never consult it, so a
change that cannot compile lands on disk and the model discovers it only by calling
`cs_diagnostics` afterwards, if it calls it at all. The design settles five questions before the
implementation: which languages, staged file or whole project, what happens with a diagnostic the
model cannot fix, how verification stays bounded and visible, and where the check runs. #184
originated the design; this signed-off implementation is tracked by #336.

## What changes

This is a **design-only** change. It adds no production code, tests or migration. It records the
five decisions in `design.md`, a delta that adds a `verified-edits` capability spec, and the
maintainer-approved ADR-0025. The five decisions are:

- **(a) Which languages — C# first.** The first slice verifies `.cs` files through the C# verifier
  registered by `Lunate.Roslyn` with the public write-verifier seam. TypeScript is tracked as #340:
  the LSP extension registers a verifier for `*.ts`/`*.tsx`.
- **(b) Staged content, project compiled — not a file alone, not a build.** The loaded project is
  compiled in memory with source generators run; the diagnostic filter is the staged file. In a
  multi-targeted project, only the TFM loaded by the workspace is checked. Dependents are out of
  scope. A `dotnet build` writes artifacts and is slower; the measured Roslyn changed-file check on
  this repo is **0.01–2.6 s warm**.
- **(c) A diagnostic the model cannot fix — refuse only what the edit introduced.** Compare
  baseline and staged errors as multisets keyed by `(code, message)`; refuse when a count increases.
  The two-refusal escape hatch remains, but refusal text does not advertise it. `verify: block | warn
  | off`, default `block`: `warn` writes and reports unverified with diagnostics; `off` skips checks.
- **(d) Staying visible — a budget, a ceiling, and loud degradation.** The implementation
  recalibrates the ceiling by ADR-0005 and pins it in the perf suite. The TUI live area shows
  `verifying… <elapsed>`; Esc cancels the run with no write. A timeout degrades to unverified with
  a named reason.
- **(e) Where the check runs — a public verifier seam.** `Lunate.Extensibility.Abstractions`
  registers file-pattern verifiers declared in the manifest. The host calls the verifier after
  approval, between read/normalize and apply/write, with a bounded time. It returns verified,
  unverified with reason, or refused with diagnostics in `StructuredContent`; it cannot write or
  modify content. `Lunate.Roslyn` registers the C# verifier. The `ToolCalling` hook and extension
  tool rejections remain; the public seam is the third option without their problems.

## Capabilities

### Modified Capabilities

_none_

### New Capabilities

- `verified-edits`: the verified-write contract — which runs, what refuses, what does not, and how
  an over-budget check degrades.

## Impact

- **Contract surface (later implementation):** a public, manifest-declared write-verifier
  registration in `Lunate.Extensibility.Abstractions`, tracked in `PublicAPI.Unshipped.txt`.
- **Host and verifier (later implementation):** the host invokes registered verifiers within the
  write/edit mutation boundary; `Lunate.Roslyn` supplies the C# verifier through that API.
- **Docs (later implementation):** `docs/guide.md` (the "agent knows before it writes" line the
  implementation changes) and `docs/architecture.md`'s write-path flow get an update.
- **Tests:** none in this design change. The implementation change adds red-green tests for the
  verifier contract and registration, introduced diagnostics, generated members, the loaded TFM,
  settings, refusal escape hatch, timeout and cancellation.
- **Risk:** design only; the maintainer signed off the five recommendations and ADR-0025 on
  2026-10-30 with the amendments recorded in this change.

## Out of scope

- Production code, tests, fixtures and any migration (the later implementation change adds them).
- **TypeScript verifier** — tracked as #340; the LSP extension will register a verifier for
  `*.ts`/`*.tsx`.
- **Whole-solution verification per edit** — the 7.8 s measured cost buys dependents' errors that
  (c) not block anyway; dependent breakage stays visible through `cs_diagnostics`, which will be
  invoked and reported after.
- A `ToolCalling` hook and an extension tool as mechanisms; their rejection reasons and the chosen
  public verifier seam are recorded in design §6 and ADR-0017's addendum.
