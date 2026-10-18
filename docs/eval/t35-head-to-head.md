# T-35 / T-35b — Head-to-head C# eval: Lunate vs Claude Code + C# LSP vs OpenCode vs pi vs tau

**Date:** 2026-10-10 (T-35) · extended 2026-10-18 (T-35b: + pi, + tau) · **Phase:** 5 · **Suite:** the six-task fixture suite (`eval/tasks/`, unchanged since T-17)

The phase-5 gate: the same C# task suite through Lunate, Claude Code with its C# LSP plugin, and OpenCode with its C# tooling — extended in T-35b with **pi** and **tau** — pass rate, steps, tokens, edit tiers, wall time, failure categories. One results row per tool in `eval/results.csv`.

## Environment and setup steps

| Tool | Version | Setup needed |
| --- | --- | --- |
| Lunate | build of `main` @ `4cfca85` (post-T-28) | `dotnet build src/Lunate.Coding -c Release` |
| Claude Code | 2.1.292 (`claude-opus-5-5` default model) | **C# LSP plugin**: `claude plugin install csharp-lsp` (official directory, plugin v1.0.0) + `dotnet tool install --global csharp-ls` (0.28.0) on `PATH`; headless: `claude -p … --output-format json --dangerously-skip-permissions` |
| OpenCode | 1.18.35 | none — C# LSP (csharp-ls) is built in; headless: `opencode run --format json --auto -m opencode-go/deepseek-v4.1-flash` |
| pi | 1.3.0 (npm `@earendil-works/pi-coding-agent`) | none; headless: `pi -p --mode json --no-session --provider openrouter --model deepseek/deepseek-v4.1-flash` |
| tau | 0.4.8 (`uv tool install tau-ai` — the Python port of pi) | minimal user model overlay (`~/.tau/catalog.toml`, no pricing entry) for the model id; headless: `tau -p --mode json --provider openrouter --model deepseek/deepseek-v4.1-flash` |

All runs: fresh `mktemp -d` per task, `repo/` copied in, the task's `task.md` handed to the tool verbatim, the same `check.sh` verifying afterwards, serial execution (no CPU contention between runs). Model parity where possible: Lunate, OpenCode, pi and tau all ran `deepseek-v4.1-flash`; Claude Code ran its configured default (Anthropic-only — noted in every comparison below). The T-35b runs (pi, tau) executed with `PATH="/usr/local/share/dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"` because the user-level .NET install on this machine hangs `dotnet run` (a local muxer defect unrelated to the tools under test; `check.sh` and the tools' own builds use the same SDK content either way).

## Results

| Tool | Model | Passed | Steps | Tokens | Wall | Cost | Edit tiers |
| --- | --- | --- | --- | --- | --- | --- | --- |
| lunate | deepseek/deepseek-v4.1-flash | **6/6** | 41 | **78,439** | 74.4 s | — | `exact;indent` |
| claude | claude-opus-5-5 | **6/6** | **24** | 451,025 | 74.2 s | $0.784 | n/a |
| opencode | opencode-go/deepseek-v4.1-flash | **6/6** | 32 | 377,966 | 76.3 s | $0.012 | n/a |
| pi | deepseek/deepseek-v4.1-flash | **6/6** | 33 | 84,839 | 62.5 s | $0.004 | n/a |
| tau | deepseek/deepseek-v4.1-flash | **6/6** | 38 | 101,724 | 80.8 s | — | n/a |

Steps = model round trips (`usage_updated` events / `num_turns` / `step_finish` / `turn_end` events — the suite's definitions, approximately comparable). Tokens = summed traffic (Claude's includes cache creation + cache reads). Lunate's provider usage does not carry a per-run cost in its event stream; at deepseek-v4.1-flash pricing its 78k tokens are on the order of pi's $0.004. tau's cost field is zero because its catalog overlay carries no pricing for the model id.

### Per task (steps / tokens)

| Task | lunate | claude | opencode | pi | tau |
| --- | --- | --- | --- | --- | --- |
| csharp-failing-test | 7 / 12,754 | 5 / 103,117 | 7 / 84,072 | 9 / 28,192 | 9 / 29,778 |
| csharp-feature-add | 6 / 10,079 | 4 / 61,521 | 6 / 70,931 | 5 / 12,690 | 7 / 19,473 |
| csharp-multi-file-repair | 9 / 16,561 | 3 / 60,063 | 4 / 46,798 | 5 / 11,687 | 5 / 10,949 |
| csharp-rename | 10 / 24,866 | 5 / 103,077 | 6 / 70,958 | 5 / 11,748 | 7 / 18,117 |
| csharp-runtime-bug | 4 / 5,470 | 4 / 62,237 | 4 / 46,324 | 4 / 8,470 | 5 / 10,709 |
| python-average | 5 / 8,709 | 3 / 61,010 | 5 / 58,883 | 5 / 12,052 | 5 / 12,698 |

## Failure categories

**None.** All 30 runs passed their checks: no load/restore/SDK failures, no symbol-position errors, no wrong edits, no other failures in any arm. The suite is saturated at this size — it cannot discriminate reliability between the tools, only efficiency and behaviour.

## Observations

- **Lunate — token efficiency, chatty loop.** ~5× fewer tokens than Claude Code and OpenCode for the same 6/6 (78k vs 378k/451k) and the lowest of all five tools, zero tool errors, and the edit-matching fallbacks were genuinely exercised (`exact` everywhere, `indent` on two tasks). It also takes the most steps: the loop checks compilation after edits (bash) and keeps tool calls small, trading round trips for context size. The Roslyn tools (`cs_find_references`, `cs_outline`, `cs_rename`) were available but never chosen — at this task scale plain reads/edits were enough.
- **Claude Code — fewest steps, most tokens and cost.** It batches work per turn (edit + build + test inside one turn) and solved everything Bash-first: file discovery via `grep`/`cat`, and the rename via `sed -i ''` (BSD sed — worked, and `check.sh` verified it). Its C# LSP was installed and available but **was not used in any run**: the `LSP` tool is deferred (needs ToolSearch to load) and read-only/navigation-only — it cannot rename. On this suite "Claude Code + C# LSP" behaved as "Claude Code + bash". At $0.78 it cost ~65× OpenCode's run.
- **OpenCode — middle.** Built-in csharp-ls fed diagnostics in-loop; ~5× Lunate's tokens for the same result; 32 steps; ~$0.01 on the Go subscription route.
- **The `cs_rename` comparison scenario.** All five passed the rename task. No arm used a symbol-aware rename tool: Claude Code's LSP cannot rename, and Lunate's model chose three plain edits (the task's two call sites and the definition) plus compile checks. The comparison at this scale measures task outcome, not rename machinery.
- **pi — the efficiency peer (T-35b).** 33 steps / 84.8k tokens — within 8% of Lunate's traffic at a similar step count, at $0.004. Like Claude Code it works bash-first (185 bash vs 56 edits of 311 calls) but without the context cost: the minimal harness (small prompt, no plugins) keeps the traffic in Lunate's class. This is the strongest evidence yet that the token gap is **harness weight, not model** — pi and Lunate share a design philosophy, not a codebase.
- **tau — the same family, a step behind (T-35b).** pi's Python port: 38 steps / 101.7k tokens, more read-heavy (114 reads vs pi's 70). Close enough to count as one category, consistently heavier within it.
- **The category split.** The three lightweight agents — Lunate 78k, pi 85k, tau 102k — sit 4–4.5× under OpenCode (378k) and Claude Code (451k) for the same 6/6, with four of five on the same model. Whatever the positioning ends up saying, "lightweight" is a measurable category, not a slogan.

## Conclusion

By the guide's own gate — *"Lunate has to win on reliability and steps, not only work"* — the head-to-head does **not** show Lunate clearly better on C# tasks: reliability is a tie (all 6/6 across five tools, suite saturated), and Lunate takes the most steps. What it clearly wins is token efficiency and the edit-tier machinery held up in practice; OpenCode, on the same model, shows the same pass rate at ~5× the tokens — so the harness's context economy is a real, measurable advantage, while "agent-shaped C# tools beat editor-style requests" remains unproven at this suite size. T-35b sharpens the picture: pi and tau — the minimal-harness family — land in Lunate's token class (85k / 102k vs 78k), so the ~5× gap separates lightweight harnesses from heavy ones, not Lunate from the field.

The guide's rule for this outcome is to move the positioning to **.NET-native extensibility and the daily-driver experience** instead of C# intelligence — and the T-35b evidence supports the sharper framing the maintainer asked for: *lightweight coding agents every C# developer can customise*. Recommended, with one caveat: the suite is too small to be conclusive about reliability — a harder task set (larger solutions, more call sites, real symbol-position traps) would either rescue or bury the C#-intelligence bet. The positioning decision itself is recorded for the maintainer (T-35b's guide edit is drafted for sign-off).

## Reproduce

```bash
dotnet build src/Lunate.Coding -c Release
scripts/eval.sh --model deepseek/deepseek-v4.1-flash --phase 5
scripts/eval.sh --tool claude --phase 5
scripts/eval.sh --tool opencode --model opencode-go/deepseek-v4.1-flash --phase 5
scripts/eval.sh --tool pi --model deepseek/deepseek-v4.1-flash --phase 5
scripts/eval.sh --tool tau --model deepseek/deepseek-v4.1-flash --phase 5
```

pi/tau need `OPENROUTER_API_KEY`; on a machine whose user-level .NET install hangs `dotnet run`, prefix the runs with `PATH="/usr/local/share/dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"` (the `check.sh` steps call `dotnet run`).
