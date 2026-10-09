# Design: print mode, non-interactive approval and the eval baseline (T-17)

## Print mode contract

- CLI: `lunate -p "<prompt>"` with optional `--json` and `--yolo` (flags accepted in any order; `-p`/`--print` is one token; the prompt is the next argument). Missing prompt or unknown flags = usage error on stderr, exit 2, nothing on stdout.
- **stdout, non-JSON**: exactly the final answer — the text of the last assistant message with content, written once at the end (guide: "writes the final answer to stdout and exits"). Intermediate assistant text and tool activity do **not** reach stdout, so `lunate -p "..." > answer.txt` captures a clean answer.
- **stdout, `--json`**: one JSON object per line, every event in arrival order (the full stream, for scripts and CI). Nothing else on stdout.
- **stderr, both modes**: human diagnostics only — one line per denied or error tool call ("tool 'bash' denied: approval policy 'ask' (pass --yolo to run unattended)"), plus warnings. Never JSON.
- **Exit codes**: 0 = `RunFinished(stop)`; 1 = `RunError` or an exception before a clean finish (including model/config errors); 2 = `RunFinished(length | step_limit)`; 130 = cancelled (SIGINT).
- The run is driven by consuming `AgentHarness.RunAsync` — print mode is a consumer, it adds no core knowledge. SIGINT cancels the run token and still exits 130 after cleanup.

## JSONL event schema

One object per line, `SystemTextJson` defaults, invariant, no pretty printing. Common fields when present: `type`, `runId`, `sessionId`, `parentRunId`, `source` (only when not `"core"`). Per type (camelCase): `run_started`; `run_finished`+`stopReason`; `run_error`+`message`; `text_message_start|content|end`+`messageId` (`content` +`text`); `tool_call_start|args|end`+`callId` (+`toolName` on start, +`args` string on args, +`parentToolCallId`); `tool_call_result`+`callId`+`output`+`isError` (+`details` as a JSON element, the tool's detail record — the eval reads `matchTier` from edit details); `approval_requested`; `usage_updated`+`usage`; `retrying`; `compaction_applied`; `step_limit_reached`; `tool_progress_update`. A dedicated `PrintEventJson` serializer maps with a switch over the sealed event records (compile-time exhaustiveness); unknown future events fail the build, not the runtime.

## Model, auth and config resolution

- Model: `LUNATE_MODEL` env > `settings.model` > error with an actionable message (mentions settings.json, the env var, and `--discover`). No model id is hardcoded.
- The catalog loads with the user overlay (`ModelCatalog`); the chosen model must exist in the catalog or the error names it and points at `--discover`.
- Credentials: the T-16 seams — provider env over auth.json, `AuthRef` for custom endpoints; the CLI wires `AuthStore` into the factory's named-key source. Secrets never appear in output (errors name keys).
- Harness options: `SystemPrompt` = composed (T-16 — template + facts + AGENTS.md chain), `WorkingDirectory` = process cwd, `ToolOutputLimit` = resolved settings, `ModelCatalog`/`ModelId` for compaction, `Approver` = the non-interactive approver below. Tools: read, write, edit, bash (the T-11..T-15 set) — C# tools join when/if the eval calls for them.

## Non-interactive approval

- Settings vocabulary corrected to the guide: `approval` ∈ {`ask` (default), `auto-edit`}; `yolo` in settings.json is a validation error ("never a saved setting"); `--yolo` is per-run only.
- `NonInteractiveApprover` (in `Lunate.Coding`) maps policy × `ToolRisk`:
  - `ask`: ReadOnly allowed; Write and Execute denied.
  - `auto-edit`: ReadOnly and Write allowed; Execute denied.
  - `--yolo`: everything allowed.
- Denials use the existing mechanism (error tool result the model can read and adapt to) plus a stderr diagnostic line. This is a deliberate approximation: the interactive policy's tracked-file refinement ("every write outside tracked git files" asks at `ask` level) lands with the approval flow card; the design records that deviation here.
- Note for the record: `auto-edit` in print mode + `--yolo` for unattended runs matches the guide's security section; the eval runs use `--yolo` inside fixture temp directories only.

## Session attachment

Every print run persists a session through the T-13 store: id = chronological, filename-safe (`yyyyMMdd-HHmmss-<4 random chars>`, invariant, UTC); path via `SessionPaths.ForRepository(...)` when inside a repository (the same identity helper the store already uses) else `SessionPaths.ForProject(cwd)`; `Session.Create(dir/<id>.jsonl, cwd, ...)`. `--json` includes `sessionId` on events, so runs are inspectable afterwards.

## Eval suite

- Layout: `eval/tasks/<task-name>/` = `task.md` (the prompt), `check.sh` (runs in the task's working copy; exit 0 = pass, anything else = fail), `repo/` (the fixture files copied to a temp workdir). First set: 6 tasks — 5 C# (failing test fix with `dotnet test`; runtime-behavior bug fix; feature add with a `dotnet run` assertion; a rename across files; a multi-file repair) and 1 Python (`python3` check script; no pytest dependency).
- Runner `scripts/eval.sh`: options `--model <id>`, `--filter <name>`, `--phase <n>`, `--lunate-bin <path>` (default: `src/Lunate.Coding/bin/Release/net10.0/lunate.dll`, invoked via `dotnet`; error tells the user to build or run verify). Per task: fresh `mktemp -d`, copy `repo/`, run `LUNATE_MODEL=<id> <lunate> -p --json --yolo "$(cat task.md)"` with cwd = the copy, capture stdout to `events.jsonl` and stderr to `run.log`; after the run, execute `check.sh` in the copy. Parse the JSONL with `python3` (stdlib `json`) — steps = count of `usage_updated` (model calls), total tokens = sum of usage counts, seconds = wall clock, edit tiers = distinct `matchTier` values from `tool_call_result` details of the `edit` tool. Append one row per task run? No — one row per suite run: pass count plus the sums, and a `notes` field naming failures. Temp dirs are kept on failure (path printed), removed on pass (`--keep` keeps all).
- `eval/results.csv` columns (header committed once): `date,phase,tool,model,tasks,passed,pass_rate,steps,total_tokens,seconds,edit_tiers,notes`. Dates ISO (`date -u +%Y-%m-%d`), decimals invariant (padded by hand in awk/python — the writer is python, so formatting is exact).
- Fixture isolation: `eval/tasks/Directory.Build.props` stub (empty, comment-explained) stops MSBuild's walk-up so the repo's warnings-as-errors props do not apply to intentionally-flawed fixtures; `eval/` is added to `.csharpierignore`; fixtures stay out of `lunate.sln` and out of all repo gates. Check scripts are hermetic (no network) and must pass in the fixture's *broken* state? No — check scripts assert the *fixed* contract; they fail before the agent runs only when the task itself requires (the runner records the result regardless).
- Offline proof: the runner is exercised end-to-end against a stub `--lunate-bin` that replays a canned JSONL line set, proving the pipeline without network. Then the live baseline run happens with the maintainer-chosen cloud model, and the resulting row is committed (the card's gate).
- `eval/README.md`: task format, check-script contract, runner usage, row schema — the T-35 write-up uses it.

## Scope and hygiene

- No new packages (python3 for the runner is a tooling dependency, documented; existing environment).
- PublicAPI: `Lunate.Coding` untracked; `Lunate.Agent` untouched (no new public API expected; if session-id generation needs a helper it stays internal).
- Culture: JSONL, dates, decimals all invariant; suite also runs under de-AT (tests use fixed temp paths and fake clients).
- Files: `src/Lunate.Coding/PrintMode.cs`, `PrintEventJson.cs`, `NonInteractiveApprover.cs`, `Cli.cs` wiring; `SettingsStore.cs` (vocabulary); `tests/Lunate.Coding.Tests/PrintModeTests.cs`, `NonInteractiveApproverTests.cs`, settings tests updated; `scripts/eval.sh`, `eval/README.md`, `eval/tasks/**`, `eval/results.csv`; `.csharpierignore`.

## Deviations

(Filled during apply.)

- **Session attachment**: `Session.Create` owns id generation (the id is not injectable through the public API), so `PrintMode` creates the session at a provisional path, renames the file to `SessionPaths.SessionFileName(session.SessionId)` and reloads it. The stored file is `dir/<id>.jsonl` and the header id matches the name; no internal id helper and no `Lunate.Agent` change were needed.
- **Serializer exhaustiveness**: `AgentEvent` lives in another assembly, so the compiler cannot prove the switch exhaustive (CS8509 does not fire across assemblies). `PrintEventJson` covers every sealed record and a reflection test (`Every_concrete_event_type_is_handled`) pins the handled set against the assembly, so a new event type fails the test run instead of the build.
- **Test seams**: `PrintModeOptions` carries `Factory`, `SettingsPath`, `ModelsPath`, `AuthPath`, `SessionDirectory`, `WorkingDirectory` and `Environment` so tests are hermetic (temp dirs, fake clients, never the real `~/.lunate`). Production leaves them null.
- **Eval results path**: `scripts/eval.sh` writes `eval/results.csv` by default; `LUNATE_EVAL_RESULTS=<path>` overrides it so the offline stub proof does not pollute the committed file.
- **Smoke run (task 2.5)**: performed with a local OpenAI-compatible stub server on `127.0.0.1` and `HOME` pointed at a temp dir (no external API, no secrets): plain mode printed exactly `Local smoke run OK.` (19 bytes, no trailing newline) with empty stderr and exit 0; `--json -p "Say hello"` streamed `run_started, text_message_start, text_message_content, text_message_end, run_finished` with `stopReason: stop` and a matching `sessionId`, exit 0.
