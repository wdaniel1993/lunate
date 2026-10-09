# Eval suite

The task suite is the scoreboard for real quality (T-17): small coding tasks with a check script each, driven by `lunate -p --json --yolo`. Results land in `results.csv`, one row per suite run; the T-35 head-to-head uses the same tasks and rows.

## Task format

Each task is a directory under `tasks/`:

| File | Purpose |
| --- | --- |
| `task.md` | The prompt handed to the agent verbatim (`"$(cat task.md)"`). |
| `check.sh` | Runs inside the task's working copy with `bash`; exit 0 = pass, anything else = fail. |
| `repo/` | The fixture files copied into a fresh temp workdir before the run. |

The fixtures are intentionally flawed: the agent's job is to make `check.sh` pass. Fixture projects set their own `TargetFramework`; `tasks/Directory.Build.props` is an empty stub that stops MSBuild's walk-up, so the repository's warnings-as-errors settings do not apply. `eval/` is excluded from CSharpier, is not part of `lunate.sln`, and is out of every repository gate.

Check scripts use no live APIs and need no network, except `csharp-failing-test`: its `dotnet test` restores `xunit.v3` from NuGet on a cold cache (warm caches run offline). C# tasks use `dotnet test`, `dotnet run` or `dotnet build`; the Python task uses `python3 -m unittest` (stdlib only, no pytest). A fixture must fail its check before the fix and pass after it.

## Running the suite

```bash
dotnet build src/Lunate.Coding -c Release
scripts/eval.sh --model <exact-model-id> --phase 3
```

| Option | Meaning |
| --- | --- |
| `--model <id>` | Required; exported as `LUNATE_MODEL` and pinned in the row. |
| `--filter <task>` | Run one task by directory name. |
| `--phase <n>` | The `phase` column value (default `3`). |
| `--lunate-bin <path>` | The build to run; a `.dll` is invoked via `dotnet` (default `src/Lunate.Coding/bin/Release/net10.0/lunate.dll`). |
| `--keep` | Keep every task working copy, also on pass. |

Per task the runner makes a fresh `mktemp -d`, copies `repo/`, runs `lunate -p --json --yolo` there with stdout in `events.jsonl` and stderr in `run.log`, then runs `check.sh` in the same copy. Working copies are kept on failure (the path is printed) and removed on pass. `LUNATE_EVAL_RESULTS=<path>` overrides the results file (used by the offline stub proof).

Metrics come from the JSONL: `steps` = number of `usage_updated` events (model calls), `total_tokens` = sum of the usage counts (`totalTokenCount`, else input + output), `seconds` = wall clock of the agent run, `edit_tiers` = the distinct `matchTier` values of `edit` tool results.

## Row schema

`results.csv` header (committed once): `date,phase,tool,model,tasks,passed,pass_rate,steps,total_tokens,seconds,edit_tiers,notes`.

One row per suite run: ISO date (UTC), phase, tool (`lunate`), the exact model id, task and pass counts, `pass_rate` with three decimals, summed steps/tokens/seconds, the distinct edit tiers (`;`-separated, `none` when no edit ran), and `notes` naming failed tasks. Rows are written with Python's `csv` module, so quoting is exact and the decimals are invariant.
