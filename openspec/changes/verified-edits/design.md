# Design — verified edits: compile before write, diagnose before apply

- Status: approved — 2026-10-30 (maintainer sign-off; implementation tracked by #336)
- Change: `verified-edits` (#184)
- Evidence: measured timings on this repository (§1.2), the landed extension contract
  (`openspec/specs/extensions/spec.md`), and the supported `lsp-diagnostics` extension
  (`extensions/lsp-diagnostics/`) that already demonstrates the post-write half of the idea
- Amends: ADR-0017 (public write-verifier extension point), with ADR-0006 (in-process
  Roslyn), ADR-0014 (backend interface), ADR-0021 (per-workspace services, laziness), and ADR-0025
  as context; see [`adr.md`](adr.md)
- Design only: no production code, tests or fixtures are part of this change

## 1. Constraints and the measured baseline

### 1.1 What exists today

- **The write path is a queue.** `WriteTool`/`EditTool` are `ITool`s of risk `Write`
  (`src/Lunate.Coding/WriteTool.cs:22`, `:65`, `src/Lunate.Coding/EditTool.cs:23`, `:78`); their
  read-modify-write runs inside `IFileMutationQueue.RunAsync` — the process-wide
  `FileMutationQueue.Shared` (`src/Lunate.Coding/EditTool.cs:175`, `:22`) — and that is where the
  on-disk text is read, the replacement computed and the bytes written, atomically per path.
- **The diagnostics engine already exists and is per workspace.** `cs_diagnostics` compiles the
  loaded Roslyn solution without a build (ADR-0006/0014); backends are created lazily per canonical
  root and LRU-capped (`src/Lunate.Coding/WorkspaceServices.cs:39`, `:75`,
  `src/Lunate.Coding/HarnessFactory.cs:280`); `DiagnosticsCollector` collects errors and warnings
  with 1-based positions, capped at 200
  (`src/Lunate.Roslyn/DiagnosticsCollector.cs:12-21`). `ICSharpBackend.NotifyFileChanged` lets the
  backend re-read one file (`src/Lunate.Roslyn/ICSharpBackend.cs:48`).
- **A guard precedent lives in the tools.** `BashGuard`/`BashChangeTracker` sit inside the bash
  tool's own path in `Lunate.Coding` — core-internal guards. Verified edits deliberately use a
  different placement: a public extension seam, not another core guard.
- **The post-write half already exists as an extension.** The supported `lsp-diagnostics`
  extension attaches a language server's diagnostics to the result of every successful
  `write`/`edit` of a `.cs` file through a `ToolResultReady` hook — one lazy server process,
  quiet-on-failure, `timeoutMs`-bounded (`extensions/lsp-diagnostics/README.md`). It informs after
  the fact; it never blocks.
- **The hook envelope is small by contract.** Hooks run under a per-handler timeout and a slow
  threshold of 500 ms (`openspec/specs/extensions/spec.md:251`); `ToolCalling` failure policy is
  fail-safe block, `ToolResultReady` keeps the original on a transform failure
  (`openspec/specs/extensions/spec.md:220`, `:238`).

### 1.2 The measured facts (this repo)

Measured 2026-10-30 on this worktree, macOS arm64, SDK 10.0.402, warm OS caches, per the rules
above:
`MSBUILDDISABLENODEREUSE=1`, `-nodeReuse:false`, `DOTNET_CLI_UI_LANGUAGE=en`. Roslyn timings were
taken by driving `lunate mcp roslyn` over stdio with a throwaway Python harness (JSON-RPC, one
timed request per call; monotonic clock). The solution is `lunate.slnx`: 39 `.csproj` files, 59
Roslyn projects (multi-targeting), 1 649 documents; the load is **partial** with 12 workspace
failures this repo already carries.

| Check | Wall time |
| --- | --- |
| `dotnet build` whole solution, no-op incremental | 41 s (54 s when some work) |
| `dotnet build` one project (`Lunate.Coding`), no-op | 10.7 s; 13.7 s after a one-file change |
| Roslyn: cold server, first check (loaded solution + changed-file scope) | 28.7 s |
| Roslyn: cold server, first full-solution scope | 70.5 s (load + first whole-solution pass) |
| Roslyn: warm, unchanged changed-file scope | 13–18 ms |
| Roslyn: warm, one real edit → changed-file scope | 2.6 s |
| Roslyn: warm, unchanged whole-solution re-check | 1.0–1.2 s |
| Roslyn: warm, after one edit → whole-solution scope | 7.8 s |
| tools/list (no Roslyn load) | 13–21 ms |

Scale reference: spike S-4 measured a fixture load at 0.64 s and Polly (21 projects/47 Roslyn
projects) at 3.3 s load, 90–110 ms warm edit-to-diagnostics (`adr/0006-roslyn-placement.md:16-18`).
This repository is the slow end of the range; the budget rule absorbs that.

## 2. (a) Which languages — C# first; the second language already has a home

**Options.**

1. **C# via the in-process Roslyn backend only** — everything the check needs exists; nothing new
   starts per session beyond what `cs_diagnostics` already starts (ADR-0021 laziness: nothing
   loads before the first `cs_*` call, `adr/0021-per-run-workspaces.md:25`).
2. **C# plus TypeScript via an LSP server** — the tempting second. TypeScript has no in-process
   Roslyn: it needs a language-server process, an LSP client and a second lifecycle decided
   (ADR-0006 rejected an out-of-process engine for C# because in-process met the latency bar —
   for TypeScript there is no in-process alternative, which is exactly why it is a separate
   decision, not an appendix).

**Recommendation: option 1 alone in the first slice.** The measured cost of C# verification is
2.6 s warm (§1.2); nothing is purchased for TypeScript with one slice's budget. TypeScript is
tracked as **#340**: the LSP extension registers a verifier for `*.ts`/`*.tsx` through the same
public seam. It is not part of this first slice. Explicitly out of the first slice: gating on
analyzer warnings (§4), any second engine, any server-process management in core.

## 3. (b) Staged file or whole project — compile the project, check the file

A file alone does not typecheck: identifiers, members and imports resolve against the whole
compilation, which is the project. The four candidate checks, with this repo's numbers:

| Option | Measured cost (this repo) | What it also costs |
| --- | --- | --- |
| Text-diff only, no type check | ~0 ms | No verification at all; rejected as noise. |
| `dotnet build` (project level) | 10.7 s no-op; 13.7 s after one edit | Writes `obj`/`bin`, races bash builds, restore/SDK coupling per ADR-0006. |
| `dotnet build` (whole solution) | 41–54 s | Same, paid per check. |
| Roslyn: staged text compiled in memory, whole-solution diagnostics | 7.8 s warm-after-edit | Dependents' errors the refusal rule (§4) may never act on anyway. |
| **Roslyn: staged text compiled in memory, changed-file scope** | **2.6 s warm-after-edit; 13 ms unchanged; ≥28.7 s cold first load** | Dependents' breakage is not checked (on purpose, see below). |

**Recommendation: the last row.** The check swaps the staged text for the file's text in the
loaded project (the mechanism rename planning already shapes: the backend produces an in-memory
candidate solution without writing disk, `src/Lunate.Roslyn/RenamePlanner.cs` /
ADR-0014's "the forked solution is compared only") and compiles that project in memory. The unit
of *type check* is the project; the unit of *diagnostic filtering* is the staged file. The cost is
per check only what changes: 2.6 s worst warm case measured, ~1 s typical after an edit,
milliseconds when the file's diagnostics are unchanged.

Source generators run as part of the in-memory compilation, so generated members are available to
the candidate check. The workspace's loaded target framework moniker (TFM) is the one checked in a
multi-targeted project; other target frameworks are not checked. Editing a public API can break
dependents, and dependents' errors are out of scope: they surface through `cs_diagnostics` rather
than refusing this file's write. This keeps the check scoped to the edited project and file.

## 4. (c) A diagnostic the model cannot fix — refuse only what the edit introduced

**The three candidate behaviours.** (i) Refuse the write whenever the staged text carries an error
it cannot fix → the refusal is replenishing a lock-out; a file that already had those errors on
disk can never be edited again. (ii) Report and write anyway → no protection at all. (iii) Write
and tell afterwards → that is `lsp-diagnostics` today, valuable, but it does not stop the cost
(disk now holds a state that cannot compile, and the user's window shows it).

**Recommendation: baseline-compare and refuse only introduced compiler errors — the gate is that
the diagnostic is *new*, not that one exists.**

- **Baseline.** Before applying, compile the file's on-disk text in the loaded project (same
  mechanism as the candidate) and collect its errors; then compile the staged text and collect its
  errors. Compare the baseline and staged diagnostics as multisets keyed by `(code, message)`,
  line-independent. An error is introduced only when the count for its key increases; a second
  identical error therefore refuses even when one identical error was already present.
- **What refuses:** for a staged file, a new compiler error. The refusal is the error result the
  tool already returns (`new ToolResult(text, IsError: true)`) — the model reads the file, line,
  code and message, and what to do next.
- **What never refuses:** pre-existing errors (reported with their positions, then the write
  proceeds), warnings and analyzer findings (always proceed), dependents' errors (always proceed),
  diagnostics the backend could not map to the staged file.
- **The escape hatch.** After two refusals of the same file in one run, the next attempt writes
  unverified with a notice. The refusal message lists the introduced errors but does not advertise
  the bypass. A stubborn non-deterministic diagnostic (rare, but real: platform-specific misses)
  costs two tool calls, not a blocked turn — the refusal stays a gate, never a lock.
- **The `verify` setting.** `verify: block | warn | off`, default `block`. `block` refuses
  introduced errors. `warn` writes and reports the result as unverified with the diagnostics. `off`
  skips verification entirely.
- **The check cannot become an attack.** It compiles in memory; it runs no process, no network, no
  shell; it does not block on anything the model could poison; it reads only the workspace it is
  already trusted to read.

## 5. (d) Staying visible — a budget, a ceiling, and a loud degradation

**The budget rule, in the ADR-0005 spirit** (`adr/0005-budget-calibration.md:10-13`): budgets are
derived from measured medians with documented headroom, and re-checked when the baseline shifts.
For verification the measure is the *warm* check, since the cold first load is paid on a different
ledger (see below). Measured: 13–18 ms unchanged, 2.6 s after an edit, 7.8 s solution-scope after
an edit.

- **p50 and ceiling.** The requirement is a **p50 target for the warm changed-file check** —
  on this repo 1–3 s after an edit, ~20 ms unchanged — and a **ceiling**. Recommended
  value: **ceiling 10 s** (≈4× the measured warm worst case); the implementation change
  recalibrates by the ADR-0005 rule and pins the value where it is enforced.
- **What happens when it is exceeded.** At the 10 s ceiling the verifier is cancelled and the
  write proceeds unverified; its result names the elapsed time, ceiling, and timeout reason. This
  timeout degradation is never silent. Esc during verification cancels the run as usual and does
  not write the staged content.
- **The cold first load.** ADR-0021: “Nothing loads before the first `cs_*` call.” A `.cs` read or
  edit is not a `cs_*` call, so ADR-0021 as written forbids starting the C# workspace load in the
  background from such a read or edit. Left out; it would need an ADR-0021 amendment, which the
  maintainer gives. Verification does not prime the backend at startup or initiate its load. If no
  earlier `cs_*` call loaded the workspace, verification degrades to unverified with a
  workspace-not-loaded reason; after the explicit C# call, edits can use the loaded workspace.
- **How the user learns.** The TUI live area shows `verifying… <elapsed>` while the bounded call is
  active. The result states verified, verified with existing diagnostics, or unverified with its
  reason. Esc during verification cancels the run as usual, with no write.

## 6. (e) Where the check runs — the public write-verifier seam

The extension contract just landed (built-ins, per-workspace services, host-evaluated activation,
aliases, and the hook catalogs: `ToolCalling`/`ToolResultReady` among them) — that is exactly what
makes the three candidate placements distinct, and one of them small:

| Placement | Assessment |
| --- | --- |
| A `ToolCalling` hook | Rejected. The hook envelope is ≤ 500 ms configurable, slow-above default 500 ms; failure policy is fail-safe **block** — the opposite of the unverified-continue a seconds-long check requires (§5). It also runs before approval and cannot own the mutation queue or allow-list. |
| An extension tool | Rejected. An extension cannot veto a `write` the model already called for; the contract has no extension-side veto over another tool's call. `lsp-diagnostics` runs after the write (`ToolResultReady`): informing is compatible, gating is not. |
| **A core guard calling `ICSharpBackend`** | Rejected. It recreates the core→Roslyn coupling the 0.2 migration removed and makes “built only on the public API” false. The `BashGuard` precedent remains core-internal and is not the placement for verified edits. |
| **A write-verifier extension point** | Chosen. `Lunate.Extensibility.Abstractions` exposes registration of a verifier for manifest-declared file patterns. It is generic, can be implemented by `Lunate.Roslyn`, and does not put Roslyn in core. |

The host calls the registered verifier **after approval**, inside the serialized write/edit
operation, between “read and normalize” and “apply and write.” The call has a bounded duration. A
verifier may return **verified**, **unverified** with a reason, or **refused** with diagnostics in
`StructuredContent`; it has no write access and cannot modify staged content. The host alone applies
or refuses the mutation. `Lunate.Roslyn` registers the C# verifier through this seam and uses the
existing per-workspace backend. This is a public contract, not a core-internal guard.

The flow:

```text
Agent loop ──╶ tool call: write/edit (.cs) ╶──> [approval: risk Write as today]
                    │
                    ▼
     ┌── write/edit host path ───────────────────────────────────────┐
     │  resolve, approve, read + normalize, compute staged content    │
     │  (serialized mutation: exact read/check/apply sequence)         │
     │                    │                                          │
     │                    ▼                                          │
     │  match manifest-declared verifier file patterns                │
     │                    │                                          │
     │                    ▼                                          │
     │  bounded verifier call (no content write access)               │
     │       ┌────────────┼─────────────────┐                         │
     │       ▼            ▼                 ▼                         │
     │    VERIFIED    UNVERIFIED         REFUSED                      │
     │       │       + reason/status   + StructuredContent             │
     │       │            │            (introduced diagnostics)        │
     │       └──────┬─────┘                 │                         │
     │              ▼                       └─> no write              │
     │      apply and write                                           │
     │      (host owns mutation)                                      │
     │                                                                │
     │  2 refusals/file/run ──> third attempt writes unverified        │
     │  `verify: block|warn|off`; Esc cancels run with no write        │
     └────────────────────────────────────────────────────────────────┘
                    │
                    ▼
        [file-change bus: existing marks ──> cs_diagnostics sees it
         (`WorkspaceServices.FileChanges.cs:104-108`)]
```

The per-workspace backend is the same one `cs_diagnostics` composes; the verifier uses an already
loaded workspace, the staged text is the post-normalization text the write would apply, and the
check is in-memory and does not race the write it feeds. The existing hook and extension-tool
rejections above remain in force; this seam is the third placement and avoids both problems. The
implementation change specifies the concrete registration API while preserving the no-write-access
contract.

## 7. Rollout

| Phase | Change | Contract state |
| --- | --- | --- |
| 1 (this change) | Design only: proposal, delta spec, this design, accepted ADR-0025 | Not implemented; no production code |
| 2 | Implement the public verifier seam, host call after approval, and C# verifier registration; red-green tests first for contract, diagnostics, generators, TFM selection, settings, timeout/cancel and refusal escape; pin the recalibrated ceiling in the perf suite | Feature lands behind tests; budget documented |
| 3 | #340: the LSP extension registers a verifier for `*.ts`/`*.tsx` | TypeScript remains unverified until then |

## 8. Deliberate non-goals

- No TypeScript verifier, no other language verifiers, no MCP/protocol surface change in this slice.
- No whole-solution per-edit verification and no analyzer gating; warnings and analyzer findings
  never refuse, by the same lock-out argument as §4.
- No priming of the Roslyn backend at startup (ADR-0021 laziness stands).

## 9. Verification of this change

- `openspec validate --all --strict` passes.
- `python3 scripts/docs-sync --check` passes (no generated regions change).
- `npx --yes markdownlint-cli2@0.23.3` passes for the ADR (openspec/ is excluded from linting).
- Every claim about current code carries a `file:line`; every claim about cost carries the
  measurement and how it was taken (§1.2).
- No production file, test or fixture is touched.
