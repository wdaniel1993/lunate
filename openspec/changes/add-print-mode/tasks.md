# Tasks: print mode, non-interactive approval and the eval baseline (T-17)

## 1. Settings reconciliation and the non-interactive approver

- [ ] 1.1 `SettingsStore`: `approval` ∈ {`ask`, `auto-edit`} (replaces T-16's placeholder `auto`; never consumed, so no migration); `yolo` rejected with a clear error ("never a saved setting"); env override validated the same; tests updated + new rejection test
- [ ] 1.2 `NonInteractiveApprover` (Coding): policy × `ToolRisk` mapping (ask: read-only only; auto-edit: + write/edit; `--yolo`: all); unit tests for the full matrix; denial text actionable

## 2. Print mode

- [ ] 2.1 `PrintMode.cs`: flag parsing (`-p|--print`, `--json`, `--yolo`; any order; usage errors → stderr, exit 2); model resolution (env > settings > actionable error); catalog + AuthStore wiring into the factory seam; composed system prompt; session attached (new id helper if needed, internal); tools registered
- [ ] 2.2 `PrintEventJson.cs`: exhaustive switch over the sealed events → one JSON object per line; `details` as JSON element; invariant; unit tests pin the schema for every event type (golden lines)
- [ ] 2.3 Run loop consumption: final answer to stdout (last non-empty assistant text), diagnostics to stderr (denials/errors one line each), exit codes (0 stop / 1 error / 2 length|step_limit / 130 cancelled); SIGINT cancellation
- [ ] 2.4 Tests with the replay/fake client: clean run (answer on stdout, empty stderr, exit 0); tool run with --json (JSONL order, sessionId present); denial path (ask denies write; stderr line; error result visible); exit code 2 on step limit; usage/`--help` text updated
- [ ] 2.5 Live local smoke run (real terminal call, local model not required — documented in the change log; no secrets in output)

## 3. Eval suite

- [ ] 3.1 `eval/tasks/` first set — 6 tasks (5 C#: failing-test fix (`dotnet test`), runtime bug (dotnet run assertion), feature add, cross-file rename, multi-file repair; 1 Python); each with `task.md`, `check.sh`, `repo/`; stub `Directory.Build.props` at `eval/tasks/`; `eval/` added to `.csharpierignore`
- [ ] 3.2 `scripts/eval.sh` + `eval/README.md` + `eval/results.csv` header; runner exercised END TO END against a stub binary (offline red→green: stub JSONL → parsed row printed; no live call)
- [ ] 3.3 Check scripts validated: each fixture fails its check before the fix (red) and passes after a hand-fix (green) — proven per task, recorded

## 4. Baseline run (live)

- [ ] 4.1 With the maintainer-confirmed model/key: run `scripts/eval.sh --model <id> --phase 3` against the full first set; results row appended to `eval/results.csv` (exact model id pinned); sanity-check steps/tokens/edit tiers against the JSONL; commit the row

## 5. Close

- [ ] 5.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-print-mode --type change --strict`; self-review; commit per group; no push
