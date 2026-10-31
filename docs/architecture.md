# Architecture

The visual overview of Lunate: what the pieces are, how a run flows, and where each detail lives. Each diagram stays small on purpose — it shows the shape, and links to the spec or ADR that owns the detail. A change that alters a pictured structure updates this page in the same change (see the `repo-quality` spec).

## System map

```mermaid
flowchart TD
    Coding["Lunate.Coding<br/>the lunate executable"] --> Agent["Lunate.Agent<br/>loop, tools, events"]
    Coding --> Protocols["Lunate.Protocols<br/>MCP client, ACP server"]
    Coding --> Tui["Lunate.Tui<br/>terminal UI"]
    Coding --> RoslynBuiltIn["Lunate.Roslyn.BuiltIn<br/>manifest + typed factory"]
    Coding --> ExtHost["Lunate.Extensibility<br/>extension host"]
    Coding -. "built-in descriptor" .-> ExtHost
    RoslynBuiltIn --> Roslyn["Lunate.Roslyn<br/>semantic C# backend"]
    RoslynBuiltIn --> ExtApi["Lunate.Extensibility.Abstractions<br/>contract + activation evaluator"]
    Roslyn --> ExtApi
    Coding -. "evaluates manifest rule" .-> ExtApi
    Protocols --> Agent
    Agent --> Ai["Lunate.Ai<br/>model pipeline, catalog"]
    Ai --> Providers(["Provider APIs<br/>OpenAI-compatible, Anthropic"])
    Protocols -. "stdio / HTTP" .-> Mcp(["MCP servers<br/>separate process"])
    Protocols -. "ACP" .-> Editor(["Editor (ACP client)<br/>separate process"])
```

Solid arrows are project references; dashed arrows are runtime composition or connections to separate processes. `Lunate.Coding` is the composition root. It supplies the Roslyn built-in descriptor to the extension host and runs the shared activation filter, which evaluates the manifest rule through the public abstractions API for chat, print, ACP, child runs and MCP. The adapter owns the typed factory and bridges the extension contract to `Lunate.Roslyn`. `Lunate.Coding` references the adapter only to provide the built-in factory list, and neither Roslyn project references upward into the host. The adapter and backend do not load Roslyn/MSBuild assemblies until the first C# tool call. See [ADR-0026](../adr/0026-roslyn-built-in-boundary.md) and the [extensions spec](../openspec/specs/extensions/spec.md).

## The model pipeline

```mermaid
flowchart LR
    Loop["AgentHarness (the loop)"] --> OTel["OpenTelemetry"] --> Log["logging"] --> Acc["accumulator"] --> Rec["recorder"] --> Prov["provider adapter"]
    Prov --> Api(["provider API"])
    Rec -. "LUNATE_RECORD=1" .-> Jsonl["recordings (JSONL)"]
    Replay(["ReplayChatClient"]) -. "replaces recorder and provider" .-> Acc
```

Order matters: telemetry is outermost (model spans see everything below it), then logging, the accumulator (consumers only ever see complete function calls), the recorder and the provider. Recording is opt-in via `LUNATE_RECORD=1`. Replay is opt-in via `LUNATE_REPLAY=<fixture path>`: the fixture becomes the client with no credential resolved and no provider client constructed (a recording request wins when both are set), so a replayed run still goes through telemetry, logging and the accumulator, like the tests' recorder decorator seam. Homes: [ai-layer spec](../openspec/specs/ai-layer/spec.md), [ADR-0002](../adr/0002-layering-and-meai-model-types.md), [ADR-0010](../adr/0010-anthropic-adapter.md).

## One run

```mermaid
sequenceDiagram
    participant C as Consumer
    participant H as AgentHarness
    participant P as IChatClient pipeline
    participant T as ITool
    participant V as IWriteVerifier (Extensibility.Abstractions)

    C->>H: RunAsync(userInput)
    H-->>C: RunStarted
    loop until the answer has no tool calls
        H->>P: stream (history and tool declarations)
        P-->>H: updates
        H-->>C: TextMessageStart / Content / End
        opt tool calls
            H-->>C: ToolCallStart / Args / End
            H->>T: ExecuteAsync(args, context)
            opt approved write/edit with a matching verifier
                T->>V: VerifyAsync(immutable request, cancellation token)
                V-->>T: verified / refused / unverified + diagnostics
            end
            T-->>H: ToolResult
            H-->>C: ToolCallResult
        end
    end
    H-->>C: RunFinished (stop or step_limit)
    Note over H: spans: invoke_agent per run,<br/>execute_tool per call, model spans nested
```

`AgentHarness.RunAsync` is the only entry point; the consumer is the TUI, print mode, the ACP mapper or a test. The loop keeps going until a model answer has no tool calls or `MaxSteps` (default 50) is reached — then `StepLimitReached` and `RunFinished(step_limit)`. One `invoke_agent` span wraps the run; `execute_tool` spans hang off it; model-call spans nest under it. Homes: [agent-loop spec](../openspec/specs/agent-loop/spec.md), [ADR-0003](../adr/0003-loop-own-vs-maf-harness.md), [ADR-0012](../adr/0012-tool-contract.md), [ADR-0013](../adr/0013-agent-loop.md).

For a matched verifier, the `write`/`edit` tool calls it after approval and before applying staged
content; the request cannot replace that content. `verify` defaults to `block`, which prevents a
write on refusal; `warn` writes and reports unverified with diagnostics, while `off` skips the check.
Verification is bounded to 10 seconds; a timeout writes as unverified. Esc cancels the run through
the usual cancellation path and leaves the staged content unwritten. The verifier contract is
`IWriteVerifier` in `Lunate.Extensibility.Abstractions`.

## The event path

```mermaid
flowchart LR
    Emit["loop and tools<br/>(IAgentEvents.Emit)"] --> Ch["AgentEventChannel<br/>ordered, unbounded"]
    Ch --> Stream["RunAsync stream<br/>IAsyncEnumerable of AgentEvent"]
    Stream --> Tui2["TUI (T-22)"]
    Stream --> Print2["print mode (T-17)"]
    Stream --> Acp2["ACP mapper (T-27)"]
    Stream --> Tests2["recorders and tests"]
```

Every event of a run — the loop and the tools alike — travels one ordered channel, and `RunAsync` exposes it as a stream. The TUI, print mode and the ACP mapper are consumers (later cards); tests record the same stream. Home: [agent-events spec](../openspec/specs/agent-events/spec.md); the AG-UI and ACP mapping table lives in the [guide](guide.md).
