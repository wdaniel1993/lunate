# Sigma — A Native C# Coding Agent for the Terminal

Implementation guide · Oct 3, 2026 · @Daniel

## Purpose, goals and non-goals

Sigma (working name) is a fast, native terminal coding agent in C#, built as a daily driver: a reliable core loop, four core tools, a Spectre-based TUI and first-class C# support through Roslyn. It is a personal, MIT-licensed project, and it is built card by card with Claude Code from this guide.

**Why it should exist next to Pi and OpenCode**

- **Zero-setup, reliable C#:** Roslyn is built in, with no plugin or language server to install. Compile checks after every edit, and refactor-class tools (find references, rename) as first-class agent tools with mandatory diffs. Claude Code and OpenCode reach C# through LSP plugins, which need setup and have known reliability problems.
- **One file, no runtime to install:** self-contained single file, fast start, no Node or Python runtime, and extensions loaded at runtime like Pi's.
- **Editor and local-model friendly:** runs inside Zed or JetBrains via ACP and works with any OpenAI-compatible endpoint, including the home lab.

These claims age quickly, so the Phase 5 gate tests them head to head: the same C# task suite with Sigma, Claude Code with its C# LSP plugin, and OpenCode with its C# tooling. If Sigma does not win on C# tasks, it stays a personal tool and a learning project, which is fine.

**Goals**

- A working agent `sigma` with interactive TUI, print mode and ACP mode, for OpenAI-compatible, Anthropic and local models.
- Reliability first: the `edit` tool tolerates harmless formatting differences but never applies an ambiguous change, and every failure is a message the model can act on.
- Self-contained single-file releases (ReadyToRun) for osx-arm64, win-x64 and linux-x64 (more targets on demand), held to startup and memory budgets in CI.
- Semantic C# tools through Roslyn, loaded only when first needed, and runtime extensions for tools, slash commands and event handlers.
- Plain, readable C# in small files; no hard line-count target.
- Buildable by Claude Code from task cards, with rules enforced by analyzers, hooks and CI.

**Non-goals**

- Sub-agents, plan mode, long-term memory.
- Own IDE plugins (ACP instead) and any web frontend.
- Hosting as an embeddable agent server: no AG-UI endpoint, no A2A. Using the MAF Harness internally is a separate question, decided by spike S-1. `Sigma.Agent` stays a clean library, without product promises.
- Supporting every provider: OpenAI-compatible, Anthropic and local via OpenAI-compatible endpoints are enough.

## Design principles

Every feature request and every Claude Code plan is checked against these seven rules; when in doubt, leave it out or make it an extension.

1. **Primitives, not features.** The core ships four tools: `read`, `write`, `edit`, `bash`. Everything else (Roslyn, MCP) is opt-in.
2. **Robust, never silently wrong.** Tools tolerate harmless differences (line endings, indentation) but refuse anything ambiguous. Every fallback is visible: the result says which match tier was used and the TUI shows the diff.
3. **Performance budgets are a gate.** Startup time and idle memory are measured in CI on every commit. Heavy components (Roslyn, MCP servers, extensions) load on first use, never at startup.
4. **Small system prompt.** Under 1,000 tokens, asserted in a test. Project rules come from `AGENTS.md`, not from the harness.
5. **The core knows nothing about UIs or protocols.** `Sigma.Agent` emits events; the TUI, print mode and ACP mode are consumers.
6. **Everything is replayable.** Sessions are append-only JSONL. A session file plus a recorded model stream reproduces a run exactly, so tests never need API keys.
7. **Spec before code.** Public types, file formats and protocol mappings are written in this guide or an ADR before Claude Code implements them. Code that disagrees with the spec is a bug in one of them, and the spec is fixed first.

## Architecture

Three core projects with strict downward dependencies, like Tau's `tau_coding → tau_agent → tau_ai`, built on Microsoft.Extensions.AI. Around them: a TUI library, a protocols project on the MCP and ACP SDKs, and Roslyn as a built-in extension that loads on first use.

&#91;embedded content: Sigma architecture · 3 core layers, TUI, protocols, Roslyn extension, external MCP servers\]

Arrows point from a project to what it depends on or calls; the dashed box is a separate process. `Sigma.Protocols` references `Sigma.Agent` (MCP tools become `ITool`s, ACP drives the loop); `Sigma.Tui` references no other Sigma project; `Sigma.Roslyn` is loaded through the extension loader, so Roslyn and MSBuild assemblies are not touched until the first C# tool call.

**Solution layout**

```text
sigma/
  AGENTS.md                    # rules for any coding agent (source of truth)
  CLAUDE.md                    # @AGENTS.md + Claude Code notes
  .claude/settings.json        # permissions + hooks
  .claude/hooks/               # format-changed.sh, quick-verify.sh
  .claude/skills/              # next-card, verify, eval, adr
  .claude/agents/reviewer.md   # read-only review subagent
  .editorconfig
  global.json                  # pins the .NET SDK
  Directory.Build.props        # net10.0, nullable, warnings as errors, PublicAPI analyzers
  docs/spec/                   # this guide, one file per section
  docs/tasks/                  # T-xx.md cards + index.md (status per card)
  docs/decisions/              # ADR-xxx.md
  docs/spikes/                 # spike code and reports (S-1 to S-4)
  scripts/verify.sh            # + verify.ps1: build, tests, publish, budgets, API and format checks
  scripts/perf.sh              # startup and memory budgets
  src/Sigma.Ai/                # IChatClient setup, model catalog, record and replay clients
  src/Sigma.Agent/             # loop on IChatClient, events, ITool, sessions, compaction, extension contract
  src/Sigma.Tui/               # Spectre output, own input line and live area
  src/Sigma.Protocols/         # MCP client and ACP server on the SDKs
  src/Sigma.Roslyn/            # built-in extension: semantic C# tools, loaded on first use
  src/Sigma.Coding/            # exe: sigma (self-contained single file): TUI, print and ACP modes, extension loader
  tests/Sigma.*.Tests/         # one test project per src project
  tests/fixtures/streams/      # recorded provider streams (JSONL)
  tests/fixtures/sessions/     # golden session files
  tests/fixtures/edit-corpus/  # edit tool cases
  tests/fixtures/solutions/    # small C# solutions for Roslyn tests
  tests/tools/TestMcpServer/   # tiny MCP server for protocol tests
  eval/tasks/                  # fixture repos + check scripts
  eval/results.csv
```

Layering is enforced, not just documented: an architecture test fails on any upward `ProjectReference`, and `PublicApiAnalyzers` turns every public API change into a visible diff in `PublicAPI.Unshipped.txt`.

## Tech stack

Stick to .NET 10, Microsoft.Extensions.AI and a few well-known packages. No Native AOT: models and networks dominate latency, and JIT keeps runtime extensions, in-process Roslyn and every SDK available. Startup and memory are held by budgets instead.

| Concern | Choice | Project | Why |
| --- | --- | --- | --- |
| Runtime | .NET 10, C# 14, SDK pinned in `global.json` | all | Current LTS |
| Release build | Self-contained single file, ReadyToRun, compression on, three targets | Sigma.Coding | One file, no runtime install, fast start |
| Model access | `IChatClient`, `ChatMessage`, `ChatResponseUpdate` everywhere; no `FunctionInvokingChatClient` (the loop runs tools) | Sigma.Ai, Sigma.Agent | The .NET standard, maintained by Microsoft |
| Providers | OpenAI-compatible via the Microsoft.Extensions.AI OpenAI adapter; Anthropic via an `IChatClient` implementation (check the official SDK first) | Sigma.Ai | One abstraction for every model |
| Telemetry | OpenTelemetry via `IChatClient` middleware + own `ActivitySource`, opt-in | Sigma.Ai, Sigma.Agent | Traces of model and tool calls |
| JSON | `System.Text.Json`; `AIJsonUtilities` options for Microsoft.Extensions.AI types; source generation where it is cheap | all | Fast, consistent |
| CLI parsing | `System.CommandLine` | Sigma.Coding | Standard |
| Output rendering | `Spectre.Console` | Sigma.Tui | Robust markup, tables, panels, colours |
| Input line and live area | Own code on `System.Console` + VT sequences | Sigma.Tui | Typing while the agent streams |
| Markdown | Own subset renderer to Spectre renderables | Sigma.Tui | Spectre has no Markdown widget |
| Diffs | Own Myers line diff (about 150 lines) | Sigma.Tui | No extra dependency |
| MCP client | Official MCP C# SDK | Sigma.Protocols | MCP tools arrive as Microsoft.Extensions.AI functions |
| ACP server | Community SDK (`AcpSdk`, `AgentClientProtocol` or `LibAcp`) behind `IAcpServer`; choice recorded in an ADR in T-27 | Sigma.Protocols | No hand-written protocol |
| Roslyn | `Microsoft.CodeAnalysis.CSharp.Workspaces`, `MSBuildWorkspace`, `Microsoft.Build.Locator`, in process, loaded on first use | Sigma.Roslyn | No sidecar needed without AOT |
| Extensions | `AssemblyLoadContext` per extension | Sigma.Coding | Runtime loading like Pi |
| Process execution | `System.Diagnostics.Process` behind `IShell` | Sigma.Coding | Easy to fake |
| API tracking | `Microsoft.CodeAnalysis.PublicApiAnalyzers` | libraries | Public API changes become visible diffs |
| Tests | xUnit v3, `Verify`, `Spectre.Console.Testing` | tests/ | Snapshots of events, sessions, screens |
| Agent framework | Microsoft Agent Framework Harness, only if spike S-1 says so | Sigma.Agent | See below |

**Spikes (task T-03)**

Each spike is throwaway code in `docs/spikes/S-x/` with a short report and an ADR.

| Spike | Question | Result |
| --- | --- | --- |
| S-1 MAF Harness | Can Sigma's loop be the Microsoft Agent Framework Harness instead of our own? | ADR: own loop, harness, or borrow parts |
| S-2 Git Bash input | Does raw key reading work for a .NET app inside mintty? | ADR: supported, or documented fallback |
| S-3 Startup baseline | Single-file ReadyToRun hello-world on all OSes: startup and memory | Calibrated budgets for `scripts/perf.sh` |
| S-4 MSBuildWorkspace reality check | Does in-process Roslyn load real solutions reliably and fast enough, also from the published single-file build? | ADR: go, move Roslyn out of process, or narrow the C# claim; calibrated Roslyn budgets |

**S-1 in detail.** Build a minimal harness agent with our four tools and every optional harness feature (todos, modes, web search, file memory, file access) switched off. Check six things:

1. Startup and idle memory against our own loop.
2. System prompt size with everything off: under 1,000 tokens?
3. A complete event stream for the TUI and ACP, including steering and `Esc` cancel.
4. Approval hooks: per tool, per arguments, "always for this session".
5. Sessions stored and replayed from recorded streams, deterministically.
6. API churn: breaking changes in the harness over the last releases.

Default is our own loop on `IChatClient`. Switch to the harness only if it passes 2 to 5; if it fails some, borrow its parts (compaction strategy, approval middleware) instead.

**S-4 in detail.** The C# differentiator rests on in-process Roslyn beating the plugin paths, and loading workspaces is historically the painful part. Use the fixture solution plus one large real solution, and check:

1. `MSBuildLocator` finds the right SDK on all three OSes, including a machine with several SDKs and a `global.json` pin.
2. Cold load time and memory for both solutions.
3. Edit to diagnostics latency after a change made through the `edit` tool.
4. Behaviour on design-time build problems: missing restore, unsupported project types, broken references.
5. All of the above again from the published single-file build. As far as I know, recent `MSBuildWorkspace` versions run design-time builds in a separate build-host process, so check that it ships and starts correctly from a single-file app.

If loading is unreliable, decide before Phase 5: fix it, move Roslyn out of process, or narrow the C# claim.

**Performance budgets (starting points, calibrated by S-3)**

| Measure | Budget |
| --- | --- |
| `sigma --version` | under 150 ms |
| First TUI frame | under 300 ms |
| Idle memory in the TUI | under 100 MB |
| First C# tool call (Roslyn load, small solution; calibrated by S-4) | under 5 s |

Adding a package is a design decision: add a row here (and an ADR) before Claude Code touches a `.csproj`.

## Layer 1: Sigma.Ai (providers)

`Sigma.Ai` builds `IChatClient` pipelines and keeps the model catalog; it defines no message types of its own. The whole core works with Microsoft.Extensions.AI types directly: `ChatMessage`, `AIContent` (text, `FunctionCallContent`, `FunctionResultContent`, reasoning), `ChatOptions` and `ChatResponseUpdate`.

**Why `IChatClient` all the way**

It is the .NET standard abstraction, maintained by Microsoft. MCP SDK tools arrive as Microsoft.Extensions.AI functions, telemetry and logging come as middleware, and there is no mapping layer to maintain. The two risks are covered below: the session format (golden tests) and streaming details (one accumulator, tested on recorded streams).

**What Sigma.Ai contains**

```csharp
public sealed record ModelInfo(string Id, string Provider, Uri? Endpoint, int ContextWindow, bool SupportsTools);

public interface IChatClientFactory
{
    // provider client -> OpenTelemetry (opt-in) -> logging -> recorder (SIGMA_RECORD=1)
    IChatClient Create(ModelInfo model);
}

public sealed class RecordingChatClient(IChatClient inner, string fixturePath) : DelegatingChatClient(inner) { }
public sealed class ReplayChatClient(string fixturePath) : IChatClient { }
```

- `ModelCatalog`: a built-in `models.json` (id, provider, endpoint, context window, tool support) merged with `~/.sigma/models.json`. Adding a model never needs a code change.
- `RecordingChatClient` and `ReplayChatClient`: record and replay `ChatResponseUpdate` streams as JSONL, serialized with `AIJsonUtilities` options.
- `ProviderErrors`: classifies exceptions as retryable or not.

**Rules for this layer**

- Never use `FunctionInvokingChatClient`: the loop runs tools itself, so approvals, cancellation and events stay under our control.
- The loop acts only on complete function calls. If an adapter streams arguments in pieces, one `StreamAccumulator` assembles them, tested on recorded streams from every provider.
- Finish reasons come from `ChatFinishReason`; an unknown value counts as stop and raises a warning event.
- No retries here. Retries live in the loop, so they show up as events.

**Recorded streams (tests never need API keys)**

Contract tests run against real endpoints only when `SIGMA_LIVE=1`. With `SIGMA_RECORD=1` the factory adds the recorder, which saves every update to `tests/fixtures/streams/<scenario>.jsonl` with a header line (model id, request hash). `ReplayChatClient` plays them back. You record fixtures once; Claude Code only replays them.

## Layer 2: Sigma.Agent (the reusable brain)

`Sigma.Agent` owns the loop, tools, events, sessions, compaction and the extension contract. It references only `Microsoft.Extensions.AI.Abstractions`, `Sigma.Ai` and the BCL.

**Tools**

Tools stay our own type, because they carry things Microsoft.Extensions.AI does not model: a risk level for approvals, a context with events, and UI-only details such as diffs. The model sees them through a declaration-only adapter; Microsoft.Extensions.AI never invokes them.

```csharp
public enum ToolRisk { ReadOnly, Write, Execute }

public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement ParametersSchema { get; }   // hand-written JSON schema, no reflection
    ToolRisk Risk { get; }                  // drives the approval policy
    Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct);
}

public sealed record ToolResult(string Output, bool IsError, object? Details = null);
public sealed record ToolContext(string WorkingDirectory, IAgentEvents Events);

// Declares an ITool to the model; invoking it through Microsoft.Extensions.AI throws.
internal sealed class ToolDeclaration(ITool tool) : AIFunction { /* Name, Description, JsonSchema */ }
```

`Details` is for the UI (for example a diff) and is never sent to the model. Tool output sent to the model is truncated by the loop (default 30,000 characters, cut in the middle with a marker). MCP tools arrive from the SDK as functions and are wrapped as `ITool`s, so they go through the same approval policy. Do not use `AIFunctionFactory`: it builds tools by reflection and hides the schema.

**Events (the only output of the core)**

Events are our own type, named and sequenced like AG-UI's so that the TUI, ACP and a possible web frontend later are thin mappings. Sigma-specific events are marked as extensions; in AG-UI they would travel as custom events. Check the AG-UI and ACP names against the pinned spec versions when writing the mappers.

| Sigma event | AG-UI | ACP | TUI |
| --- | --- | --- | --- |
| `RunStarted`, `RunFinished(stopReason)`, `RunError` | Run started, finished, error | Prompt response with stop reason | Spinner, footer |
| `TextMessageStart`, `TextMessageContent`, `TextMessageEnd` | Text message start, content, end | Agent message chunks | Streaming text |
| `ToolCallStart`, `ToolCallArgs`, `ToolCallEnd` | Tool call start, args, end | Tool call | Tool block header |
| `ToolCallResult` (with `Details`) | Tool call result | Tool call update | Tool block body, diff |
| Extension: `ApprovalRequested` | Custom | Permission request | Approval prompt |
| Extension: `UsageUpdated`, `Retrying`, `CompactionApplied`, `StepLimitReached` | Custom | Not sent, logged | Footer, notices |

The harness exposes them as `IAsyncEnumerable<AgentEvent>` from `AgentHarness.RunAsync(userInput, ct)`.

**The loop, one turn**

1. Append the user `ChatMessage` to the session; emit `RunStarted`.
2. Build the request: system prompt + session messages after compaction, and `ChatOptions` with the declarations of all enabled tools.
3. Call `GetStreamingResponseAsync`; emit text events as updates arrive; collect complete function calls through the `StreamAccumulator`.
4. No function calls: append the assistant message, emit `RunFinished`.
5. For each call: check the approval policy, execute the `ITool`, append a tool-role `ChatMessage` with `FunctionResultContent`, emit the tool events. Unknown tools and bad JSON become error results, never exceptions.
6. Back to step 2. After `MaxSteps` (default 50) emit `StepLimitReached`, then `RunFinished`.

**Cross-cutting rules**

- Cancellation: `Esc` in the UI cancels the `CancellationToken`; the loop records a cancelled tool result so the history stays valid.
- Retries: transient provider errors retry 3 times with backoff, each attempt raising `Retrying`.
- Compaction: an `ICompactionStrategy` runs when estimated tokens pass 80% of the model's context window. The first version summarizes old turns with the same model and keeps the last N turns verbatim.
- Steering: the user may type while a turn runs; the message is queued and injected before the next model call.

**Session entries**

Append-only JSONL, one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; the example shows the shape, not the exact discriminator names.

```json
{"type":"header","schema":1,"id":"s_01","cwd":"/repo","created":"2026-10-03T09:00:00Z","meai":"<package version>"}
{"type":"message","id":"e_01","parentId":null,"message":{"role":"user","contents":[{"$type":"text","text":"add tests for Calculator"}]}}
{"type":"message","id":"e_02","parentId":"e_01","model":"<model id>","usage":{"in":1834,"out":41},"message":{"role":"assistant","contents":[{"$type":"functionCall","callId":"c_1","name":"read","arguments":{"path":"src/Calculator.cs"}}]}}
{"type":"message","id":"e_03","parentId":"e_02","message":{"role":"tool","contents":[{"$type":"functionResult","callId":"c_1","result":"1  public class Calculator ..."}]}}
{"type":"compaction","id":"e_40","parentId":"e_39","summary":"...","replaces":["e_01","e_30"]}
{"type":"modelChange","id":"e_41","parentId":"e_40","model":"<model id>"}
```

**Format guard:** `tests/fixtures/sessions/` holds golden session files. A test deserializes and re-serializes each one and compares byte for byte. If a Microsoft.Extensions.AI update changes the output, the test fails: bump `schema`, add a migration, and record it in an ADR.

**Compaction**

- **Trigger:** estimated context tokens pass 80% of the model's window. Estimate is characters / 4, corrected by the last `UsageReported`. Also on `/compact`.
- **What stays verbatim:** system prompt, `AGENTS.md`, the last 6 turns, and any turn whose tool call or result would otherwise be split.
- **What is summarized:** everything older, by the same model with a fixed summarization prompt (goal, decisions, files touched, open problems), stored as a `compaction` entry. The session file keeps the full history; only the request sent to the model is shortened.
- **Never:** split a tool call from its result, or summarize the current turn.
- **Test:** a recorded long session compacts once, the next request is under 60% of the window, and replay still matches.

## Layer 3: Sigma.Coding (the app)

`Sigma.Coding` turns the brain into a coding agent: the four tools, project instructions, config, sessions on disk and two frontends.

**The four tools**

Tool results are written for the model: short, factual, and every error says what to do next.

| Tool | Input | Success output | Errors the model sees |
| --- | --- | --- | --- |
| `read` | `path`, `offset` (1-based line, default 1), `limit` (default 2,000) | Numbered lines; footer `[lines 1–2000 of 5400, use offset to continue]` | Not found; binary file (size given); directory (use `bash ls`); outside workspace |
| `write` | `path`, `content` | `wrote 42 lines to src/X.cs (created)`; diff in `Details` | Outside workspace; path is a directory |
| `edit` | `path`, `old_text`, `new_text`, optional `start_line` | `edited src/X.cs lines 40–48 (match: indent)`; diff in `Details` | Not found + closest region; ambiguous + line numbers of all matches; tier refused for whitespace-significant file; `old_text` equals `new_text` |
| `bash` | `command`, `timeout_s` (default 120, max 600) | Exit code + combined stdout/stderr, cut in the middle when too long | Timeout (process tree killed); shell could not start. A non-zero exit code is reported, not treated as a tool error |

**The forgiving `edit` tool**

Matching runs in tiers and stops at the first tier with exactly one match. A tier that finds several matches uses `start_line` to pick the one starting within 3 lines of it; still ambiguous means an error, never a guess.

1. **Exact:** `old_text` verbatim.
2. **Normalized:** CRLF and LF treated alike, trailing whitespace per line ignored.
3. **Indentation-agnostic:** leading whitespace per line ignored. `new_text` is re-indented by the offset between the first line of `old_text` and the matched first line.

Guardrails:

- Tier 3 is disabled for whitespace-significant files (`.py`, `.yaml`, `.yml`, `Makefile`, `.mk`); those get an error asking to re-read the file.
- The file keeps its line endings, encoding, BOM and trailing-newline state.
- No similarity or Levenshtein matching in v1. On "not found", the tool shows the most similar region (by line overlap) as a hint only; it never applies it.
- The result names the tier, and the TUI shows the diff. The eval records how often tiers 2 and 3 are used.

**Edit corpus (`tests/fixtures/edit-corpus/`)**

One folder per case: `input` file, `request.json`, and `expected` file or `expected-error.txt`. The tool is done when every case passes. Minimum cases:

| Case | Expected |
| --- | --- |
| Unique exact match | Applied, tier exact |
| Two exact matches, no `start_line` | Error listing both line numbers |
| Two exact matches, `start_line` near the second | Second one applied |
| CRLF file, LF `old_text` | Applied, file stays CRLF |
| UTF-8 BOM file | Applied, BOM kept |
| Trailing spaces differ | Applied, tier normalized |
| C# block indented 4 more spaces than `old_text` | Applied, `new_text` re-indented, tier indent |
| Tabs in file, spaces in `old_text` | Applied, tabs kept in untouched lines |
| Python block with indentation difference | Error, tier 3 refused |
| YAML block with indentation difference | Error, tier 3 refused |
| `old_text` empty | Error |
| `old_text` equals `new_text` | Error |
| Match at first line and at last line without trailing newline | Applied, no newline added |
| `old_text` not present, similar block exists | Error with closest region shown, file unchanged |
| Normalization makes two blocks identical | Error (ambiguous), file unchanged |
| 5 MB file | Applied in under 200 ms |

**System prompt and instructions**

- One embedded `system-prompt.md`, under 1,000 tokens: who the agent is, the four tools, "read before you edit", "run the build or tests after changes".
- Appended at runtime: OS, shell, working directory, date, and the content of `AGENTS.md` files found from the repo root down to the working directory.

First draft of `system-prompt.md` (T-16 refines it and asserts the token budget):

```markdown
You are Sigma, a coding agent working in the user's repository at {cwd} on {os}, shell {shell}.
Tools: read, write, edit, bash{extra_tools}.
- Read a file before you edit it. Prefer edit over write for existing files.
- Keep old_text in edit short but unique; include start_line if the text repeats.
- After changing code, build or run the relevant tests and report the result.
- Never touch files outside the repository. Ask before destructive commands.
- Be brief. Show what you changed and why, not every step.
{agents_md}
```

**Config**

`~/.sigma/settings.json` (default model, approval policy, output limits) and `~/.sigma/auth.json` (API keys, file permissions restricted). Environment variables override both.

**Sessions on disk**

One JSONL file per session under `~/.sigma/sessions/<project-hash>/`. Flags: `--continue` (latest session), `--resume` (pick from a list).

**Frontends**

- **Interactive mode** (`sigma`): the Sigma.Tui frontend (see Terminal UI) with streaming view, multi-line editor, `Esc` to cancel, slash commands `/model`, `/new`, `/resume`, `/compact`, `/quit`.
- **Print mode** (`sigma -p "prompt"`): writes the final answer to stdout and exits; `--json` writes every event as JSON lines for scripts and CI.
- **ACP mode** (`sigma --acp`): speaks the Agent Client Protocol over stdio, so editors like Zed or JetBrains start Sigma as their agent. Editor file reads, writes and permission prompts map onto Sigma's tools and approval policy. Nothing may write to stdout except the protocol.

**Extensions**

An extension is an assembly implementing `ISigmaExtension` (defined in `Sigma.Agent`) that registers tools, slash commands and event handlers. Sigma loads extensions at runtime, like Pi:

- From `~/.sigma/extensions/` (global) and `.sigma/extensions/` (per project), each in its own `AssemblyLoadContext`.
- Each extension has an `extension.json` (name, version, entry assembly, declared tools and commands). Manifests are read at startup; assemblies load on first use, so startup budgets hold.
- Project-local extensions run code from the repository, so they need a one-time approval per repository.
- `Sigma.Roslyn` ships as a built-in extension and uses the same mechanism.
- External tools can also come through MCP (`~/.sigma/mcp.json`, see Protocols).

## Terminal UI (Sigma.Tui)

Finished output is rendered with Spectre.Console; the input line and a small live area at the bottom are our own code. Spectre's live display does not combine well with typing, so this split keeps steering (typing while the agent streams) without writing a full renderer.

**Screen model**

- **Scrollback:** finished blocks (user message, assistant message, tool block) are written once through `IAnsiConsole.Write` and never redrawn. Native scrollback, copy and search keep working.
- **Live area:** at most about 8 lines at the bottom: the streaming tail of the current assistant text, the running tool with a spinner, the status footer and the input line. `LiveArea` redraws it with cursor-up and clear-line VT sequences.
- **Streaming:** when the streaming text outgrows the live area, completed paragraphs are rendered as Markdown and committed to scrollback; the unfinished tail stays live.

**Components**

- `MarkdownRenderer`: a subset (headings, bold, italic, inline code, lists, quotes, fenced code with a language label) mapped to Spectre renderables. Keyword highlighting for C#, JSON and shell only in v1.
- `ToolBlock`: tool name and argument summary, status, first and last lines of output, and a red/green diff panel for `edit` and `write` with the match tier.
- `ApprovalPrompt`: yes, no, always for this session; shown in the live area.
- `StatusFooter`: model, tokens, context used in percent, working directory, git branch.
- `InputLine`: multi-line editing, history in `~/.sigma/history`, bracketed paste, `Tab` completion for `/commands` and `@paths`.
- `SelectList`: model and session pickers inside the live area.

**Key bindings (one meaning each)**

| Key | Action |
| --- | --- |
| `Enter` | Send; while a turn runs, queue as a steering message |
| `Alt+Enter` | New line, also Ctrl+J (terminals cannot reliably detect `Shift+Enter`) |
| `Esc` | Cancel the running turn; nothing when idle |
| `Ctrl+C` | Clear the input; on an empty input, press twice within 2 s to quit. Never cancels a turn |
| `Ctrl+L` | Model picker |
| `Up` / `Down` | History |

**Platform rules**

- Enable VT processing on Windows at startup; test in Windows Terminal and classic conhost.
- If stdout is not a terminal or `TERM=dumb`, interactive mode exits with a hint to use `-p`.
- All terminal access goes through `IConsoleIO` (read keys, write, size, resize event), so tests run without a real terminal.

**Testing**

Spectre renderables are snapshot-tested with `TestConsole`. The input line and live area run against a `FakeConsoleIO` that replays scripted key presses and records the frames, snapshot-tested with `Verify`.

## Platforms, shells and terminals

Sigma should run wherever Pi runs: macOS, Linux and Windows, in the terminals people actually use. Support is defined per environment, tested in CI where possible, and every capability has a fallback.

**Release targets**

Self-contained single-file builds with ReadyToRun and compression for `osx-arm64`, `win-x64` and `linux-x64` (more targets on demand). Users install no .NET runtime. CI publishes each target and runs `scripts/perf.sh` on the three OS runners.

**Terminal support**

| Environment | Tier | Notes |
| --- | --- | --- |
| Windows Terminal | 1 | Full colour, VT, bracketed paste |
| Classic console (conhost, Windows 10 1809+) | 1 | VT enabled at startup; verify paste and resize |
| VS Code integrated terminal (all OSes) | 1 | Common daily environment |
| Git Bash (mintty) | 2 | .NET console input has had problems under mintty in the past; test early, document a fallback (Windows Terminal profile) |
| macOS Terminal.app | 1 | No 24-bit colour, so 256 colours; Option is not Meta by default |
| iTerm2, Ghostty, WezTerm, Kitty, Alacritty | 1 | Full features |
| Linux terminals (GNOME Terminal, Konsole, others) | 1 | Full features |
| tmux, screen, SSH sessions | 1 | Colour from `TERM`/`COLORTERM`; check bracketed paste passthrough |
| JetBrains terminal | 2 | Usually fine; ACP is the better integration there |
| Pipes, CI, `TERM=dumb` | Print mode only | Interactive mode exits with a hint to use `-p` |

**Capability fallbacks**

- Colour depth from Spectre's detection and `COLORTERM`; `NO_COLOR` disables colour everywhere.
- No Unicode support detected: ASCII spinner and box characters.
- Character widths (emoji, CJK) via a `wcwidth` table, so the input line never misplaces the cursor.
- Newline: `Alt+Enter` and `Ctrl+J`, because `Alt` needs "Option as Meta" on macOS Terminal.

**Shell resolution for the `bash` tool**

| Platform | Order | Invocation |
| --- | --- | --- |
| macOS, Linux | `bash`, then `/bin/sh` | `bash -c` (no login shell: Sigma already inherits the user's environment, and login profiles are slow or print banners) |
| Windows | Git Bash, then `pwsh` 7, then Windows PowerShell 5.1, then `cmd` | Git Bash via its install path; PowerShell with `-NoProfile -NonInteractive` |

- Models write bash best, so bash wins whenever it exists.
- Never pick `C:\Windows\System32\bash.exe`: that is WSL, which runs the command inside Linux with different paths.
- `"shell"` in `settings.json` overrides the detection; `/shell` shows the result. The system prompt and the `bash` tool description name the shell, so the model writes the right dialect.
- Child processes run with UTF-8 output; the process tree is killed with `Process.Kill(entireProcessTree: true)` on timeout or cancel.
- Workspace boundary checks are case-insensitive on Windows and on default macOS file systems.

**Installation (as easy as Pi's one-liner)**

- GitHub Releases with one archive per target, plus `install.sh` (`curl … | sh`) and `install.ps1` (`irm … | iex`).
- Homebrew tap for macOS and Linux; Scoop bucket and winget manifest for Windows.
- Optionally a `dotnet tool` package; .NET 10 a framework-dependent package for people who already have the .NET SDK.

**CI matrix**

Build and test on `ubuntu`, `macos` and `windows` runners for every commit; publish all three targets on release tags. Manual checks before each release: Windows Terminal, conhost, Git Bash, macOS Terminal, one Linux terminal, tmux over SSH.

## Protocols (Sigma.Protocols)

MCP and ACP live in `Sigma.Protocols` and use existing SDKs; without AOT there is no reason to write our own JSON-RPC.

**MCP client (official MCP C# SDK)**

- Servers are configured in `~/.sigma/mcp.json` (command, args, env) and start on first use.
- Their tools arrive from the SDK as Microsoft.Extensions.AI functions and are wrapped as `ITool`s with risk `Execute`, so the approval policy applies. Names are prefixed `server__tool` to avoid collisions.
- Refresh on tool list changes; cancel calls when a turn is cancelled; time out hanging servers. A crashing server becomes a tool error, never a crash of Sigma.
- v1 uses stdio servers; remote servers over HTTP follow when needed, using the SDK's transports.
- Out of scope for v1: resources, prompts, sampling.

**ACP server (community SDK behind an interface)**

```csharp
public interface IAcpServer
{
    Task RunAsync(Stream input, Stream output, Func<AgentHarness> createHarness, CancellationToken ct);
}
```

- T-27 compares `AcpSdk`, `AgentClientProtocol` and `LibAcp` (spec coverage, activity, license) and records the choice in an ADR. The interface keeps a later switch cheap.
- `sigma --acp` speaks ACP over stdio; nothing else may write to stdout. Logs go to stderr or `~/.sigma/logs/`.
- Handles initialize, new session, prompt (one run, streaming updates mapped from `AgentEvent`s), and cancel.
- Approvals map to the client's permission request; file reads and writes use the editor's file system when it offers that capability, so unsaved buffers are respected.

**Tests**

`tests/tools/TestMcpServer` is a tiny MCP server with two normal tools, one slow tool and one crashing tool. ACP is tested with an in-process client over a pipe pair. Before closing Phase 5, run one real session in Zed.

## Roslyn extension (the .NET edge)

`Sigma.Roslyn` is a built-in extension that gives the model semantic C# tools bash cannot match; it is the main reason this project should exist in C#.

**Loading model**

Roslyn loads in process on the first C# tool call. `MSBuildLocator` finds the installed .NET SDK, and `MSBuildWorkspace` opens the solution for the working directory and keeps it loaded for the session. File changes made by `write` and `edit` are applied to the workspace, so it stays current without reloading. Without an installed .NET SDK the C# tools are simply not offered. If memory on very large solutions becomes a problem, the same tools can move into a child process later without changing their contracts.

**Tools, in build order**

| Tool | What it returns | Why the model needs it |
| --- | --- | --- |
| `cs_diagnostics` | Compiler errors and warnings for changed files or the whole solution | Fast compile check after an edit, without a full `dotnet build` |
| `cs_find_symbol` | Definition location and signature for a type or member name | Navigates large solutions without grepping |
| `cs_find_references` | All references to a symbol | Safe changes to public APIs |
| `cs_outline` | Types and member signatures of a file, no bodies | Cheap context for big files |
| `cs_rename` | Applies a solution-wide rename, returns changed files | Refactoring that grep-and-replace gets wrong |

Phase 5 ships `cs_diagnostics` and `cs_find_symbol`, because they carry the main claim (a compile check right after an edit). The other three follow in Phase 6. Tests use small solutions in `tests/fixtures/solutions/` (a console app, and a library with a test project) so a workspace loads in seconds.

The Phase 5 gate is external (T-35): the same C# task suite with Sigma, with Claude Code plus its C# LSP plugin, and with OpenCode plus its C# tooling. Record setup steps needed, pass rate, steps, tokens, edit tiers used and wall time. Sigma has to win on reliability and steps, not only work.

**Rules**

- Outputs are compact text, not JSON dumps. Cap at the same limit as other tools.
- If the solution fails to load, tools return a clear error and the agent falls back to the four core tools.
- Measure it: run the same task set with and without `Sigma.Roslyn` and compare success rate and tokens (see Testing).

## Roadmap

Seven phases (0 to 6), each closed by a gate you check yourself. Phase 3 is the turning point: from then on Sigma can help build Sigma. The differentiators (Roslyn, ACP) come right after the TUI, so the main claim is tested early. Phase 0 also decides, through spike S-1, whether the loop is our own or the MAF Harness.

&#91;embedded content: Roadmap · 7 phases, a gate after each\]

**Task cards**

One row per card. Each becomes `docs/tasks/T-xx.md` (template below) before Claude Code starts it; `docs/tasks/index.md` tracks status. A card is ready when everything in "Depends on" is done.

| ID | Phase | Card | Depends on | Done when |
| --- | --- | --- | --- | --- |
| T-01 | 0 | Repo skeleton: solution, `Directory.Build.props`, `global.json`, `.editorconfig`, analyzers, AGENTS.md, CLAUDE.md, `.claude/` | – | `scripts/verify.sh` green on empty projects |
| T-02 | 0 | CI: build and test on 3 OSes, single-file publish for three targets, `scripts/perf.sh` | T-01 | Artifacts for all targets; CI turns red when a budget is exceeded |
| T-03 | 0 | Spikes S-1 MAF Harness, S-2 Git Bash input, S-3 startup baseline, S-4 MSBuildWorkspace reality check | T-02 | One ADR per spike; budgets calibrated |
| T-04 | 1 | `IChatClientFactory` and model catalog with user override | T-03 | Pipeline built per provider; catalog merge tests |
| T-05 | 1 | `RecordingChatClient` and `ReplayChatClient` | T-04 | Recorded fixture replays identically |
| T-06 | 1 | Providers (OpenAI-compatible, Anthropic) and `StreamAccumulator` | T-05 | Contract tests; fixtures from each provider committed |
| T-07 | 1 | `AgentEvent` types, aligned with AG-UI | T-04 | Event sequence rules tested |
| T-08 | 2 | `ITool`, `ToolDeclaration` adapter, registry, output truncation | T-04 | Schemas reach the model unchanged; truncation tests |
| T-09 | 2 | Agent loop on `IChatClient`: run, tool execution, max steps, events | T-06, T-07, T-08 | Snapshot of 3 replayed sessions |
| T-10 | 2 | Error paths: bad JSON, unknown tool, cancel, retries | T-09 | One replay test per path; history stays valid |
| T-11 | 2 | Session JSONL store, resume, golden files | T-09 | Golden files round-trip byte for byte |
| T-12 | 3 | Workspace paths, `read`, `write` | T-08 | Boundary tests incl. symlinks and case-insensitive file systems |
| T-13 | 3 | `edit` tiers 1–2 + corpus | T-12 | Corpus cases for tiers 1–2 pass |
| T-14 | 3 | `edit` tier 3, `start_line`, closest-region errors | T-13 | Full corpus passes |
| T-15 | 3 | `bash`: shell resolution per platform, UTF-8, timeout, process-tree kill, truncation | T-08 | Tests pass on all three CI runners |
| T-16 | 3 | System prompt, AGENTS.md loading, config | T-09 | Prompt under 1,000 tokens (asserted) |
| T-17 | 3 | Print mode `-p`, `--json`; first eval run | T-11 to T-16 | Baseline row in `eval/results.csv` |
| T-18 | 4 | `IConsoleIO`, live area, input line (VT, raw keys, paste) | T-03 | Frame snapshots; manual check in Windows Terminal |
| T-19 | 4 | Markdown subset to Spectre renderables | T-01 | Snapshot per Markdown feature |
| T-20 | 4 | `ToolBlock` and Myers diff rendering | T-19, T-13 | Snapshots incl. match tier |
| T-21 | 4 | Approval prompt, status footer, key bindings | T-18 | Scripted key tests for every binding |
| T-22 | 4 | Wire events, steering, `Esc` cancel, slash commands, pickers | T-09, T-18 to T-21 | End-to-end scripted session snapshot |
| T-23 | 4 | Compaction | T-11 | Replay: long session compacts; tool pairs never split |
| T-24 | 5 | Extension loader: manifests, `AssemblyLoadContext`, lazy load, project approval | T-09 | Sample extension loads; startup budget unchanged |
| T-25 | 5 | `Sigma.Roslyn` extension: `MSBuildLocator`, workspace load, file sync, `cs_diagnostics` | T-24 | Detects an error introduced by `edit`; first-call budget met |
| T-26 | 5 | `cs_find_symbol` | T-25 | Finds definitions in fixture solutions |
| T-27 | 5 | ACP server: SDK choice (ADR), initialize, session, prompt, updates, cancel | T-09 | In-process client tests |
| T-28 | 5 | ACP permissions and editor file system | T-27 | Approval round trip; one real Zed session |
| T-29 | 6 | MCP client on the official SDK: lazy start, tool wrapping, cancel, list changes | T-08 | Tests against `TestMcpServer` |
| T-30 | 6 | MCP config, prefixing, approvals, TUI integration | T-29 | One real MCP server works in the TUI |
| T-31 | 6 | `cs_find_references`, `cs_outline`, `cs_rename` | T-25 | Fixture tests; eval comparison row |
| T-32 | 4 | Distribution: release workflow, install scripts, Homebrew, Scoop, winget, `dotnet tool` | T-02, T-17 | Fresh install works on Windows, macOS and Linux with one command |
| T-33 | 4 | Terminal capability detection and fallbacks; manual terminal matrix check | T-18, T-19 | Every tier-1 terminal checked and noted in the card |
| T-34 | 6 | Extension authoring: template project, docs, one sample extension | T-24 | A new extension builds from the template and loads |
| T-35 | 5 | Head-to-head C# eval: Sigma vs Claude Code with the C# LSP plugin vs OpenCode with its C# tooling; same tasks and model where possible; pass rate, steps, tokens, edit tiers, time | T-17, T-25, T-26 | One results row per tool in eval/results.csv and a short write-up in docs/eval/ |

Independent tracks can run in parallel in separate git worktrees, for example T-18/T-19 (TUI) next to T-12 to T-15 (tools).

## Building it with Claude Code

Claude Code implements one task card per session; you own the specs, the public types and the review. The repo is set up so the important rules are enforced by tools (analyzers, hooks, CI), not only written down, because written rules get skipped and failing builds do not.

**Division of work**

- **You:** this guide and its specs, task cards, ADRs, public types in the libraries, recording provider fixtures (needs API keys), manual checks on real terminals and in Zed, eval runs on real models, final review of every diff.
- **Claude Code:** implementations, tests, corpus cases, docs updates the card asks for. It may propose spec or API changes but stops and asks.
- **From Phase 3 on:** also use Sigma itself for small cards. Every failure of Sigma on its own repo becomes a test or eval case.

**Bootstrapping (before T-01)**

1. Export this guide as Markdown and save it as `docs/guide.md`.
2. In the first Claude Code session, ask it to split the guide by `##` heading into `docs/spec/01-purpose.md`, `02-principles.md` and so on, and to create one `docs/tasks/T-xx.md` per row of the task table using the card template below. Review those files yourself; they are the contract for everything after.
3. Then run T-01. From here on, cards reference spec files instead of the whole guide, which keeps each session's context small.

**Repo files for Claude Code**

| File | Purpose |
| --- | --- |
| `AGENTS.md` | Rules for any coding agent (Claude Code, Sigma itself, others). Source of truth |
| `CLAUDE.md` | Imports `AGENTS.md` and adds Claude Code workflow notes |
| `.claude/settings.json` | Permissions and hooks, committed |
| `.claude/settings.local.json` | Your personal overrides, not committed |
| `.claude/hooks/*.sh` | Format after edits; build and fast tests before Claude stops |
| `.claude/skills/*/SKILL.md` | Repeatable procedures: `next-card`, `verify`, `eval`, `adr` |
| `.claude/agents/reviewer.md` | Read-only reviewer subagent |
| `docs/spec/`, `docs/tasks/`, `docs/decisions/` | Specs, cards with status, ADRs |
| `scripts/verify.sh` / `.ps1` | The one command that decides "done" |

**AGENTS.md**

```markdown
# Sigma: rules for coding agents

Specs live in docs/spec/. Read the files your task card lists before writing code.
The task card in docs/tasks/ defines the scope. Do only what it asks.

## Build
- .NET 10 (pinned in global.json), C# 14, nullable, warnings as errors.
- scripts/verify.sh (Windows: scripts/verify.ps1) must pass before a card is done:
  build, tests, single-file publish, performance budgets, Public API and format checks.

## Architecture
- Sigma.Ai <- Sigma.Agent <- Sigma.Protocols / Sigma.Coding. Never reference upward.
- Sigma.Tui references no other Sigma project.
- Sigma.Agent references only Microsoft.Extensions.AI.Abstractions, Sigma.Ai and the BCL.
- Model types are Microsoft.Extensions.AI types (IChatClient, ChatMessage, AIContent).
  Do not add parallel message types. Tools (ITool) and events (AgentEvent) are ours.
- Never use FunctionInvokingChatClient or AIFunctionFactory. The loop runs tools.
- Sigma.Roslyn, MCP servers and extensions load on first use, never at startup.

## Rules
- No new NuGet packages. Ask first.
- Public API changes appear in PublicAPI.Unshipped.txt. Change public types in Sigma.Ai, Sigma.Agent,
  Sigma.Tui or Sigma.Protocols only if the card says so; otherwise propose the change and stop.
- A failing golden session test means the session format changed. Stop and report; never update
  golden files to make a test pass.
- In ACP mode nothing writes to stdout except the protocol.
- Tool error messages are read by a model: say what failed and what to do next.
- Every behaviour change has a test. Tests use fixtures in tests/fixtures, never live APIs.
- Keep files under about 300 lines. Prefer plain code over abstractions.
- If the spec and the code disagree, stop and say so. Do not silently pick one.
```

**CLAUDE.md**

Claude Code can read `AGENTS.md` directly in recent versions, but not in every session, so an import from `CLAUDE.md` is the safe setup ([Claude Code memory docs](https://code.claude.com/docs/en/memory)).

```markdown
@AGENTS.md

## Claude Code workflow
- One task card per session. Start in plan mode: read the card and the spec files it lists,
  then propose files to touch, public types affected and the tests you will write. Wait for approval.
- Write the tests first, then the implementation.
- Run /verify before saying a card is done and paste its summary.
- Ask the reviewer subagent to check the diff, then fix what it reports.
- Commit as "T-xx: <summary>" and update the card's status in docs/tasks/index.md.
- If the card is ambiguous, list your questions instead of guessing.
```

**.claude/settings.json**

Allow the routine commands so Claude Code is not interrupted, ask before spec or API baseline changes, and deny pushes and secrets. Check the rule syntax against the current [hooks](https://code.claude.com/docs/en/hooks) and settings docs when you set it up.

```json
{
  "permissions": {
    "allow": [
      "Bash(dotnet build *)", "Bash(dotnet test *)", "Bash(dotnet format *)",
      "Bash(dotnet publish *)", "Bash(./scripts/verify.sh *)",
      "Bash(git status)", "Bash(git diff *)", "Bash(git log *)",
      "Bash(git add *)", "Bash(git commit *)"
    ],
    "ask": [
      "Edit(docs/spec/**)", "Edit(docs/decisions/**)",
      "Edit(**/PublicAPI.Shipped.txt)", "Bash(dotnet add package *)"
    ],
    "deny": [
      "Bash(git push *)", "Bash(rm -rf *)", "Read(./.env)", "Read(~/.sigma/auth.json)"
    ]
  },
  "hooks": {
    "PostToolUse": [
      { "matcher": "Edit|Write",
        "hooks": [{ "type": "command", "command": "${CLAUDE_PROJECT_DIR}/.claude/hooks/format-changed.sh" }] }
    ],
    "Stop": [
      { "hooks": [{ "type": "command", "command": "${CLAUDE_PROJECT_DIR}/.claude/hooks/quick-verify.sh" }] }
    ]
  }
}
```

**Hooks**

`format-changed.sh` formats a changed C# file and never blocks:

```bash
#!/usr/bin/env bash
f=$(jq -r '.tool_input.file_path // empty')
[[ "$f" == *.cs ]] || exit 0
dotnet format whitespace --folder --include "$f" >/dev/null 2>&1 || true
exit 0
```

`quick-verify.sh` keeps Claude working until the build and fast tests pass. Exit code 2 sends the message back to Claude instead of letting it stop; the `stop_hook_active` check prevents an endless loop.

```bash
#!/usr/bin/env bash
input=$(cat)
[ "$(echo "$input" | jq -r '.stop_hook_active')" = "true" ] && exit 0
out=$(dotnet build -warnaserror -v q 2>&1 && dotnet test --no-build --filter "Category!=Slow" -v q 2>&1)
if [ $? -ne 0 ]; then
  echo "Build or fast tests fail. Fix this before finishing:" >&2
  echo "$out" | tail -40 >&2
  exit 2
fi
exit 0
```

Both need `jq`; on Windows, Claude Code runs hooks through Git Bash. If the Stop hook slows down planning conversations, disable it in `settings.local.json` for that session.

**scripts/verify.sh**

The single definition of "done". CI runs the same script; the PowerShell twin does the same on Windows. `scripts/perf.sh` uses `hyperfine`, installed in CI and on your machine; memory is checked by a smoke test that starts the TUI against the replay client.

```bash
#!/usr/bin/env bash
set -euo pipefail
RID=${RID:-linux-x64}
dotnet build -warnaserror
dotnet test --no-build
dotnet publish src/Sigma.Coding -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "artifacts/$RID"
./scripts/perf.sh "artifacts/$RID/sigma"
dotnet format --verify-no-changes
git diff --exit-code -- '*PublicAPI.Shipped.txt'
echo "verify: OK"
```

```bash
#!/usr/bin/env bash
# scripts/perf.sh: fails when the startup budget is exceeded (budget calibrated by S-3)
set -euo pipefail
BUDGET_MS=${BUDGET_MS:-150}
hyperfine --warmup 2 --runs 10 --export-json /tmp/sigma-perf.json "$1 --version" >/dev/null
ms=$(jq '.results[0].median * 1000 | floor' /tmp/sigma-perf.json)
echo "startup median: ${ms} ms (budget ${BUDGET_MS} ms)"
[ "$ms" -le "$BUDGET_MS" ] || { echo "startup budget exceeded" >&2; exit 1; }
```

**Skills**

Each skill is a folder under `.claude/skills/` with a `SKILL.md`; Claude Code can use them on its own when they fit, or you call them by name.

| Skill | What it does |
| --- | --- |
| `next-card` | Reads `docs/tasks/index.md`, picks the first ready card, reads it and its specs, and starts planning |
| `verify` | Runs `scripts/verify.sh` and reports in a fixed format; never fixes anything |
| `eval` | Runs the task suite with a given model and appends a row to `eval/results.csv` |
| `adr` | Creates `docs/decisions/ADR-xxx.md` from the template: context, decision, consequences |

```markdown
---
name: verify
description: Run the full Sigma verification (build, tests, publish, budgets, API and format checks) and summarize it. Use before declaring a task card done.
---
Run ./scripts/verify.sh (Windows: pwsh scripts/verify.ps1). Report exactly:
- Build: ok, or the first 10 errors
- Tests: passed count, or the names of failing tests
- Publish and budgets: ok, or which budget failed and by how much
- Public API: unchanged, or the diff of PublicAPI.Unshipped.txt
- Format: ok, or the files to format
Do not fix anything here. Only report.
```

**Reviewer subagent (`.claude/agents/reviewer.md`)**

A second pair of eyes with a narrow brief and no write access. It catches scope creep, layering and startup mistakes before you read the diff.

```markdown
---
name: reviewer
description: Read-only reviewer for Sigma diffs. Use after implementing a task card, before committing.
tools: Read, Grep, Glob, Bash
---
Review the current diff (git diff and git diff --staged) against the task card and docs/spec.
Never edit files. Report, most serious first:
1. Scope: anything the card did not ask for.
2. Layering: references that break AGENTS.md.
3. Public API: changes in PublicAPI.Unshipped.txt the card did not allow.
4. Startup: eager loading of Roslyn, MCP servers or extensions; heavy work before the first frame.
5. Tests: behaviour without a test, tests that only assert "no exception", live API calls.
6. Errors: tool messages a model could not act on.
7. Readability: long methods, clever code, needless abstractions.
End with APPROVE, or CHANGES NEEDED and a numbered list.
```

**Task card template, with a real example**

```markdown
# T-13: edit tool, tiers 1–2
Status: ready        Phase: 3        Depends on: T-12
Specs: docs/spec/07-layer-3.md (edit tool, edit corpus), docs/spec/02-principles.md

## Goal
Implement the edit tool with tier 1 (exact) and tier 2 (normalized) as specified.

## Public API
None. EditTool is internal to Sigma.Coding and implements ITool.

## Acceptance
- [ ] Corpus cases for tiers 1–2 pass (data-driven test, one case per folder)
- [ ] File keeps line endings, encoding, BOM and trailing-newline state
- [ ] Result text and Details diff match the spec format
- [ ] 5 MB file under 200 ms (Category=Slow)

## Out of scope
Tier 3, start_line, closest-region hints (T-14). Any change to ITool.

## Verify
/verify, then the reviewer subagent.

## Notes for Claude
Add new corpus cases as files, never as inline strings.
```

**Working loop per card**

1. `/clear`, then the `next-card` skill (or name the card).
2. Plan mode: correct the plan, not the code. Typical fixes: scope creep, a missing edge case, a new public type.
3. Tests first, then implementation. The Stop hook keeps the build and fast tests green.
4. `verify` skill, then the reviewer subagent; Claude fixes what they report.
5. You read the diff, especially `PublicAPI.Unshipped.txt` and anything under `docs/spec/`.
6. Commit `T-xx: ...`, set the card to done in `docs/tasks/index.md`.
7. Anything that surprised you becomes one line in `AGENTS.md` or a new corpus or eval case.

**Definition of done (every card)**

- [ ] All acceptance items ticked in the card
- [ ] `scripts/verify.sh` green, including the performance budgets
- [ ] Reviewer subagent approved, or its remaining points are written into a follow-up card
- [ ] No public API change unless the card allowed it
- [ ] No new package without an ADR

**Prompts that help**

- "Explain how one turn flows through the files you changed, in five sentences." (readability check)
- "What happens if the model sends malformed JSON for this tool?"
- "Which corpus or eval case would have caught the bug you just fixed? Add it."
- "Delete anything in this diff the card did not ask for."

## Testing and evaluation

Tests are how Claude Code knows it is done: every level below runs without API keys except the contract tests, and the task suite is the scoreboard for real quality.

| Level | What | How | Runs |
| --- | --- | --- | --- |
| Unit | Path rules, JSON types, catalog, truncation, compaction trigger | xUnit, temp directories, fake `IShell` | Every commit |
| Edit corpus | Every edit tool case in the spec | Data-driven test over `tests/fixtures/edit-corpus/` | Every commit |
| Replay | Agent loop end to end | `ReplayChatClient` + `Verify` snapshots of events and session JSONL | Every commit |
| TUI | Renderables, input line, live area, key bindings | `TestConsole` + `FakeConsoleIO` frame snapshots | Every commit |
| Protocols | MCP client and ACP server | `TestMcpServer`; in-process ACP client over a pipe pair | Every commit |
| Roslyn | Diagnostics and symbol lookup | Fixture solutions in `tests/fixtures/solutions/` | Every commit (Category=Slow) |
| Performance budgets | Startup time and idle memory | `scripts/verify.sh` publish step and scripts/perf.sh on all three CI runners | Every commit |
| Contract | Provider adapters against real endpoints | `SIGMA_LIVE=1`; local model on the home lab costs nothing | Manual / nightly |
| Task suite | 15–20 small coding tasks (mostly C#, some Python and TypeScript) with a check script each | `sigma -p --json`; pass/fail, steps, tokens, time, edit tiers used | Per phase gate; Phase 5 also runs Claude Code and OpenCode on the C# tasks |

Results go to `eval/results.csv` per phase and model. Use the suite to decide whether Roslyn tools, a prompt change or compaction actually help, not intuition.

## Security and safety

Sigma runs model-chosen shell commands with your user's rights, so the defaults must be safe and the risk must be visible.

- **Approval policy** with three levels: `ask` (default: confirm every `bash`, every MCP tool and every write outside tracked git files), `auto-edit` (edits run, `bash` and MCP ask), `yolo` (nothing asks; needs a flag every run, never a saved setting).
- **Workspace boundary:** file tools resolve real paths (including symlinks) and refuse anything outside the working directory unless `--allow-path` is given.
- **Edit safety:** ambiguous matches are refused, never guessed; every edit shows its diff and match tier.
- **Secrets:** API keys only in `auth.json` (owner-only file permissions) or environment variables; known key patterns are redacted from tool output and session files.
- **Prompt injection:** file contents, command output and MCP results are data. The system prompt says so; approval is the real guard.
- **MCP servers** are third-party code: only servers listed in `mcp.json` start, and their output is truncated like any tool output. Extensions are third-party code too: project-local ones need a one-time approval per repository, and sigma --no-extensions starts without any.
- **ACP mode** uses the editor's permission prompts; a request coming from an editor is never auto-approved.
- **Unattended runs:** document a container setup for print mode in CI; recommend it for `yolo`.
- **No telemetry** by default; OpenTelemetry export is opt-in and goes only where you point it.

## Decisions and open questions

These decisions are settled; T-01 turns each into an ADR in `docs/decisions/` so Claude Code can find the reasoning.

| Decision | Choice | Why |
| --- | --- | --- |
| Audience | Polished daily-driver CLI; readability stays a principle | The project's value is daily use and C# depth |
| License and home | Personal open source, MIT | Free to use anywhere |
| Hosting and embedding | No AG-UI endpoint, A2A or web UI | Smaller scope, focus on the terminal and editors |
| TUI | Spectre for finished output; own input line and live area | Robust rendering and steering |
| Edit tool | Three match tiers, uniqueness required at every tier | Fewer retries without silent wrong edits |
| MCP and ACP | Official MCP C# SDK; community ACP SDK behind IAcpServer | No hand-maintained protocol |
| Keys | `Esc` cancels a turn; `Ctrl+C` clears input or quits | One meaning per key |
| Diffs | Own Myers diff | No extra dependency |
| Native AOT | No; self-contained single file with ReadyToRun, held to performance budgets | Models and network dominate latency; JIT keeps runtime extensions, in-process Roslyn and all SDKs |
| Model types | IChatClient and Microsoft.Extensions.AI types everywhere; own types only for tools and events | Microsoft-maintained .NET standard; MCP SDK fits directly |
| Agent events | Own AgentEvent, named and sequenced like AG-UI | Thin mappings to TUI, ACP and a possible web UI; no dependency on a moving protocol |
| Roslyn | In process as a built-in extension, loaded on first use | No sidecar needed without AOT |
| Extensions | Loaded at runtime from global and project folders | Pi-style extensibility |

**Resolved after review**

- [x] **Name.** Decided: Sigma as product and command name. The NuGet id sigma is taken, so packages use sigma-agent (free on NuGet; Homebrew core free; winget still to check).
- [x] **Own provider types or `IChatClient` all the way?** Revised: `IChatClient` all the way; own types only for tools and events (reasons in Layer 1).
- [x] **Shells.** Decided: broad support like Pi. Detection per platform (bash on Unix; Git Bash, pwsh, Windows PowerShell, cmd on Windows) and a terminal matrix; see Platforms, shells and terminals.
- [x] **Default model for dogfooding and the eval baseline.** Decided: a cloud model, with the exact model id pinned in every eval row (T-17).
- [ ] **Own loop or MAF Harness.** Decided by spike S-1 in T-03; default is our own loop on `IChatClient`.
