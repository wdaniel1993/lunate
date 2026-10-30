# Design — verified edits: compile before write, diagnose before apply

- Status: proposed — 2026-10-30
- Change: `verified-edits` (#184)
- Evidence: measured timings on this repository (§1.2), the landed extension contract
  (`openspec/specs/extensions/spec.md`), and the supported `lsp-diagnostics` extension
  (`extensions/lsp-diagnostics/`) that already demonstrates the post-write half of the idea
- Amends (proposed): ADR-0012 (tool contract — where a guard sits), with ADR-0006 (in-process
  Roslyn), ADR-0014 (backend interface) and ADR-0021 (per-workspace services, laziness) as context;
  see [`adr.md`](adr.md) and the proposed ADR-0025
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
  tool's own path in `Lunate.Coding` — core guards, not hooks, not extensions.
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
2.6 s warm (§1.2); nothing is purchased for TypeScript with one slice's budget. The second
language is not *out of the design* — it is out of the *first slice*, and its shape was just
proven by the supported tier: generalize `lsp-diagnostics`'s `ToolResultReady` handler to accept a
per-language server (`extensions/lsp-diagnostics/README.md` — today it filters `.cs` and assumes
`csharp-ls`). A future change can make TypeScript diagnostics post-write through that extension
**without** touching the write path's refusal semantics; pre-write gating for TypeScript would
then follow C#'s measured precedent if it proves worth the server process. Explicitly out of the
first slice: gating on analyzer warnings (§4), any second engine, any server-process management in
core.

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

**The named trade-off.** Editing a public API will break dependents, and this scope does not
report those errors — they surface afterwards through `cs_diagnostics` (which is the existing,
documented loop) rather than as a refused write. The refusal rule (§4) restates this margin:
dependents' errors never refuse this file's write, which is what makes a 7.8 s solutions-wide
per-edit check cost without a commensurate benefit.

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
  errors; a staged error refuses only when the baseline does not already carry the same error
  (compared by code and message, line-independent — a moved line keeps an existing error
  pre-existing; a brand-new code+message is introduced).
- **What refuses:** for a staged file, a new compiler error. The refusal is the error result the
  tool already returns (`new ToolResult(text, IsError: true)`) — the model reads the file, line,
  code and message, and what to do next.
- **What never refuses:** pre-existing errors (reported with their positions, then the write
  proceeds), warnings and analyzer findings (always proceed), dependents' errors (always proceed),
  diagnostics the backend could not map to the staged file.
- **The escape hatch.** After two refusals of the same file in one run, the next attempt writes
  unverified with a notice. A stubborn non-deterministic diagnostic (rare, but real: flaky
  analyzers, platform-specific misses) costs two tool calls, not a blocked turn — the refusal
  stays a gate, never a lock.
- **The check cannot become an attack.** It compiles in memory; it runs no process, no network, no
  shell; it does not block on anything the model could poison; it reads only the workspace it is
  already trusted to read.

## 5. (d) Staying invisible — a budget, a ceiling, and a loud degradation

**The budget rule, in the ADR-0005 spirit** (`adr/0005-budget-calibration.md:10-13`): budgets are
derived from measured medians with documented headroom, and re-checked when the baseline shifts.
For verification the measure is the *warm* check, since the cold first load is paid on a different
ledger (see below). Measured: 13–18 ms unchanged, 2.6 s after an edit, 7.8 s solution-scope after
an edit.

- **p50 and ceiling.** The requirement is a **p50 target for the warm changed-file check** —
  on this repo 1–3 s after an edit, ~20 ms unchanged — and a **ceiling**. Recommended
  value: **ceiling 10 s** (≈4× the measured warm worst case); the implementation change
  recalibrates by the ADR-0005 rule and pins the value where it is enforced.
- **What happens when it is exceeded.** The check is cancelled (the write is *not* cancelled); the
  tool result says the write went out **unverified**, names the elapsed time and the ceiling, and
  turns the turn back over to the model with diagnostics-visible errors expected. Degrade is from
  verified to **unverified**, never from verified to **silence**.
- **The cold first load.** ADR-0021's rule stands: nothing loads before the first `cs_*` call —
  verification will not prime the backend at startup to hide 28.7 s of cold load
  (`adr/0021-per-run-workspaces.md:25`). The first cold check either finishes within the same
  ceiling (small repos: S-4 fixture 0.64 s, Polly 3.3 s) or runs past the ceiling and degrades to
  unverified with the cold-load reason named; the next check is warm. **How the user learns:** the
  tool result states the write's verification status explicitly — verified (no new errors),
  verified with N existing diagnostics, or unverified + reason. There is no new TUI surface; the
  result text already reaches the user's transcript.

## 6. (e) Where the check runs — in the `write`/`edit` tools

The extension contract just landed (built-ins, per-workspace services, host-evaluated activation,
aliases, and the hook catalogs: `ToolCalling`/`ToolResultReady` among them) — that is exactly what
makes the three candidate placements distinct, and one of them small:

| Placement | Why it is not the smallest thing |
| --- | --- |
| A `ToolCalling` hook | The hook envelope is ≤ 500 ms configurable, slow-above default 500 ms; failure policy is fail-safe **block** — the opposite of the unverified-continue a seconds-long check requires (§5); and a hook is pre-approval: the user is asked for a call that may then block silently for seconds. Also a third-party hook can not reach the mutation queue or the allow-list. |
| An extension tool | An extension cannot refuse a `write` the model already called for: the contract names no extension-side veto over another tool's call, and `lsp-diagnostics`'s own placement is *after* the write (`ToolResultReady`, keep-original on failure). Informing is compatible; gating is not. |
| **A guard inside the tools** | The `BashGuard` precedent (`src/Lunate.Coding/BashGuard.cs`): a core guard sharing the mutation queue ordering, the workspace resolution, the allow-list and the per-workspace backend keyed by canonical root — the check is glue over what exists: acquire the backend, stage the text, compile, compare, refuse or write. |

**Recommendation: the guard inside the tools.** It is the smallest thing that works: one new
additive `ICSharpBackend` member (staged-text diagnostics and a baseline variant), one seam in
`WriteTool`/`EditTool` between "read and normalize" and "apply and write" (the exact point the
mutation queue owns), and one acquisition of the *existing* backend
(`src/Lunate.Coding/WorkspaceServices.cs:75`). No hook contract change, no manifest change, no
extension surface change, no second Roslyn, no out-of-process engine (ADR-0006).

The flow:

```text
Agent loop ──╶ tool call: write/edit (.cs) ╶──> [approval: risk Write as today]
                    │
                    ▼
     ┌── write/edit tool (Lunate.Coding) ─────────────────────────────┐
     │  resolve path, read + normalize, compute replacement text      │
     │        (mutation queue: this is one atomic per-path step)      │
     │                    │                                          │
     │                    ▼                                          │
     │  [.cs?] any .cs file ────── no ──> write as today              │
     │        │ yes                                                  │
     │        ▼                                                      │
     │  acquire backend ── fails/slow ──> UNVERIFIED + reason        │
     │        │                                                      │
     │        ▼                                                      │
     │  in-memory: baseline errors vs staged errors                  │
     │        │                         │                            │
     │   new compiler error?         none new                        │
     │        │                         │                            │
     │        ▼                         ▼                            │
     │   REFUSE (error result,    write + status line                │
     │   file/line/code/message,  (verified / existing N /           │
     │   + what to do next)        unverified + reason)              │
     │                                                               │
     │  retry cap: 2 refusals/file/run ──> subsequent writes         │
     │  unverified (never locked out)                                │
     └───────────────────────────────────────────────────────────────┘
                    │
                    ▼
        [file-change bus: existing marks ──> cs_diagnostics sees it
         (`WorkspaceServices.FileChanges.cs:104-108`)]
```

Assumptions (per the C4 method, stated because they must hold): the per-workspace backend is the
*same* one `cs_diagnostics` composes; the staged text is the post-normalization text the write
would apply; the check's compile is in-memory and does not race the write it feeds. Open
boundaries deliberately left for the implementation change: the refusal text format and the
structured-content channel (already landed in the contract for `ToolResult` details) are chosen
there, not here.

## 7. Rollout

| Phase | Change | Contract state |
| --- | --- | --- |
| 1 (this change) | Design only: proposal, delta spec, this design, proposed ADR-0025 | Not implemented; no production code |
| 2 | Implement the additive backend member and the in-tool guard; red-green tests first: refusal, pre-existing pass-through, cold-overrun degradation; pin the budget in the perf suite; calibrate the ceiling by ADR-0005's rule | Feature lands behind tests; budget documented |
| 3 | (optional, separate change) Generalize `lsp-diagnostics` to a per-language server for TypeScript post-write; a further change may then weigh pre-write gating for TS | TS unverified until then |

## 8. Deliberate non-goals and open question

- No TypeScript/LSP, no other language servers, no MCP/protocol surface change in this change.
- No whole-solution per-edit verification; no analyzer decision: which *analyzer* findings (if
  any) should ever refuse is left to the maintainer — the recommendation is none, by the same
  lock-out argument as §4.
- No hook participation and no extension-surface change; a future extension-side mechanism stays a
  separate change explicitly.
- No priming of the Roslyn backend at startup (ADR-0021 laziness stands).

## 9. Verification of this change

- `openspec validate --all --strict` passes.
- `python3 scripts/docs-sync --check` passes (no generated regions change).
- `npx --yes markdownlint-cli2@0.23.3` passes for the ADR (openspec/ is excluded from linting).
- Every claim about current code carries a `file:line`; every claim about cost carries the
  measurement and how it was taken (§1.2).
- No production file, test or fixture is touched.
