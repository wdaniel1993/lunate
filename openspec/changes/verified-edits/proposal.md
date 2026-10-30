# Verified edits: compile before write, diagnose before apply

## Why

An edit that cannot compile is a failed turn the user pays for twice: once in tokens, once in
undoing it. Lunate already has everything the check needs — the `write`/`edit` tools
(`src/Lunate.Coding/WriteTool.cs:13`, `src/Lunate.Coding/EditTool.cs:14`) and an in-process Roslyn
backend that diagnoses the loaded solution without a build (`src/Lunate.Roslyn/CsDiagnosticsTool.cs`
per ADR-0006/0014) — but the check is not connected: `write` and `edit` never consult it, so a
change that cannot compile lands on disk and the model discovers it only by calling
`cs_diagnostics` afterwards, if it calls it at all. The fix is not new tooling; it is wiring the
staged content through the existing backend **before** the write. #184 asks the design to settle
five open questions first: which languages, staged file or whole project, what happens with a
diagnostic the model cannot fix, how the check stays invisible, and where the check runs.

## What changes

This is a **design-only** change. It adds no production code, no tests and no migration. It records
the five decisions in `design.md`, a delta that adds a `verified-edits` capability spec, and a
**proposed** ADR-0025. The five decisions, each with its measured cost:

- **(a) Which languages — C# first, exactly what exists.** The first slice verifies `.cs` files
  through the in-process Roslyn backend (`ICSharpBackend`, ADR-0014), reusing RenamePlanner's
  fork-the-solution mechanism to type-check staged text without touching disk. TypeScript via an
  LSP server is explicitly out of the first slice: it needs a server process, an LSP client and a
  second lifecycle (ADR-0006 rejected that shape for C# on the same grounds).
- **(b) Staged content, project compiled — not a file alone, not a build.** The check swaps the
  staged text into the loaded project and compiles the changed project in memory. A file alone
  does not typecheck; a `dotnet build` of the project is 10.7–13.7 s incremental (41–54 s for the
  solution) and writes `obj`/`bin` artifacts that race with bash builds. The measured Roslyn
  changed-file check on this repo is **0.01–2.6 s warm** over 59 Roslyn projects / 1 649 documents.
- **(c) A diagnostic the model cannot fix — refuse only what the edit introduced.** Verification
  reports a baseline of the file's existing diagnostics and refuses only **new compiler errors**
  the staged text adds. Everything else — pre-existing errors, warnings, analyzer findings,
  dependents' errors — is reported in the result and the write proceeds. When the check cannot
  run (no solution, no SDK, cold load over budget) the write proceeds as an **unverified** write,
  never blocked, and a successor refusal is cut off by a retry cap so a refusal is never a
  lock-out.
- **(d) Staying invisible — a budget, a ceiling, and a loud degradation.** In the ADR-0005 spirit
  (median of measurements, then headroom): verification aims at the warm numbers, not the cold
  ones, and the degraded path reports why the check was skipped in the tool result
  text — degrade from verified to unverified, never to silence.
- **(e) Where the check runs — in the `write`/`edit` tools.** The smallest thing that works with
  the extension contract just landed (ADR-0017 addenda): a host-owned guard inside the write path,
  glued to the existing per-workspace backend. The alternatives are a `ToolCalling` hook
  (wrong timeout envelope — the hook slow threshold is 500 ms, the check needs seconds; and a
  blocking hook is exactly the lock-out (c) rejects) or an extension (cannot reach the mutation
  queue, file-change bus or allow-list; the contract the primitives just landed explicitly does not
  wire a new hook from the extension side).

## Capabilities

### Modified Capabilities

_none_

### New Capabilities

- `verified-edits`: the verified-write contract — which runs, what refuses, what does not, and how
  an over-budget check degrades.

## Impact

- **Contract surface (later implementation):** one additive `ICSharpBackend` member for staged-text
  diagnostics; `PublicAPI.Unshipped.txt` follows the existing single-file publish flow.
- **Host (later implementation):** a verified-edit seam inside `WriteTool`/`EditTool` (the existing
  `BashGuard`/`BashChangeTracker` pattern), acquiring the workspace's Roslyn backend through the
  existing per-workspace services registry.
- **Docs (later implementation):** `docs/guide.md` (the "agent knows before it writes" line the
  implementation changes) and `docs/architecture.md`'s write-path flow get an update.
- **Tests:** none in this change. The later implementation change adds red-green tests first —
  a staged-error write refuses, a pre-existing error writes, and a cold backend writes unverified.
- **Risk:** low — design only; the one decision that needs maintainer sign-off (the refusal rule
  (c) and where the check lives (e)) is proposed, not landed.

## Out of scope

- Production code, tests, fixtures and any migration (the later implementation change adds them).
- **TypeScript/LSP and every third language** — the slicing rule (design §2) says what the first
  slice is; adding a second language needs a new change and a decided placement for a server
  process.
- **Whole-solution verification per edit** — the 7.8 s measured cost buys dependents' errors that
  (c) not block anyway; dependent breakage stays visible through `cs_diagnostics`, which will be
  invoked and reported after.
- **The `ToolCalling` hook as the mechanism** — the hook contract's timeout obsession is the
  wrong envelope for a seconds-long check, and a hook block has no unverified-continue path.
