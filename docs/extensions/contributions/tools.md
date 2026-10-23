# Tools

A tool gives the model a callable operation. Register it with `context.RegisterTool` and a manifest
entry; it then behaves like a core tool — same context, same result, same approval policy, same
output limits.

## The API

```csharp
context.RegisterTool(new ExtensionToolRegistration
{
    Name = "bars",
    Description = "Echoes a list of integer bar values as compact JSON.",
    ParametersSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
    OutputSchema = null,                        // optional; a JSON object when present
    Annotations = new ToolAnnotations(ReadOnly: true),
    Exposure = Lunate.Agent.ToolExposure.Direct, // the default
    Risk = null,                                // optional override
    Handler = new BarsHandler(),
});
```

`ExtensionToolRegistration` fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `Name` | yes | The local name, `[A-Za-z0-9_-]+`. The model sees `<extension-id>__<name>`. |
| `Description` | yes | The text the model reads. |
| `ParametersSchema` | yes | A hand-written JSON Schema; must be a JSON object. |
| `OutputSchema` | no | The structured result's schema; a JSON object or null. |
| `Annotations` | no | MCP-style hints: `ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`. |
| `Exposure` | no | `Direct` (default), `ModelOnly`, `Programmatic`, `Deferred` or `Hidden`. The type is `Lunate.Agent.ToolExposure`; MCP registrations use the contract enum of the same name. |
| `Risk` | no | An explicit `ToolRisk`; null derives it from the annotations. |
| `Handler` | yes | An `IExtensionToolHandler`. |

The handler receives the model's `JsonElement` arguments, the run's `ToolContext` and the run's
`CancellationToken`, and returns the same `ToolResult` a core tool returns:

```csharp
public interface IExtensionToolHandler
{
    Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken ct);
}
```

## Constraints

- **Name.** The local name must be non-empty and contain only `[A-Za-z0-9_-]`. The model-facing
  name is `<extension-id>__<local name>` and may not exceed 64 characters.
- **Cap.** At most 32 tools per extension.
- **Description and schema.** The description must be non-empty; `ParametersSchema` must be a JSON
  object; `OutputSchema` must be a JSON object or null.
- **Manifest coverage.** The manifest `tools` array must list every registered local name. A
  registered-but-undeclared tool refuses the load; a declared-but-unregistered name is logged and
  dropped from the offered set.
- **Collision.** A model-facing name already registered by another extension refuses the load.
- **Registration only.** `Create` registers the handler; it must not run the tool or start
  resources. The handler runs on first call.

## Runtime behaviour

- **Approval.** The tool enters the same approval policy as core tools. Annotations drive the
  derived risk: `ReadOnly = true` becomes `ToolRisk.ReadOnly`; anything else becomes
  `ToolRisk.Write`, so the default `ask` policy asks. A null `Annotations` means no hints: the approval policy
  treats the tool as not read-only and possibly destructive (the MCP default). `Risk` overrides
  the derivation.
- **Exposure.** `Direct` is declared to the model and callable from nested execution. `ModelOnly`
  is declared but refused by `ToolContext.ExecuteToolAsync`; `Programmatic` is callable only from
  nested execution; `Deferred` is discoverable through the run's stubs; `Hidden` is neither.
- **Result.** `ToolResult.Output` and `IsError` reach the model; `Details` is UI-only;
  `StructuredContent` carries the typed result when `OutputSchema` is declared. Output is
  truncated to the run's `output.toolResultLimit` (default 30000 characters).
- **Errors.** A handler exception becomes an error result naming the tool and the message, not a
  crash: `Tool '<name>' failed: … Fix the call or try a different approach.`
- **Unload.** Unloading the extension removes its tools and cancels in-flight calls; a cancelled
  call settles as an error result saying the extension was unloaded.

## What the handler receives

`ToolContext` is the same object core tools receive. The parts extensions use:

| Member | Use |
| --- | --- |
| `RunId`, `CallId` | The current run and call identity; nested calls use `"<parent>/<n>"`. |
| `ExecuteToolAsync(name, args, ct)` | Runs a nested call through validation, hooks, approval and cancellation. |
| `Subagents` | The per-run `ISubagentRunner`; null outside a run. See the [`subagents` sample](../../../samples/extensions/subagents/README.md). |
| `Models` | The run-bound `IModelAccess`; `For(modelId)` charges usage to this run. Null outside a run. |
| `Progress` | Reports one progress line to the event stream. |
| `WorkingDirectory` | The run's working directory. |
| `FileMutations`, `FileChanges` | The per-path mutation queue and the file-change sink. |

Resolve paths against the workspace identity (`core/workspace`) when a tool can run in a child
worktree, and return teaching errors for bad arguments rather than throwing.

## Example

The `ui-showcase` sample registers one read-only tool whose model-facing name is `uikit__bars`
because its manifest id is `uikit`:

```csharp
context.RegisterTool(
    new ExtensionToolRegistration
    {
        Name = "bars",
        Description =
            "Echoes a list of integer bar values as compact JSON; the interactive session renders them as bars in the scrollback.",
        ParametersSchema = Schema,
        Annotations = new ToolAnnotations(ReadOnly: true),
        Handler = new BarsHandler(),
    }
);
```

`Schema` and `BarsHandler` are in the same file. The handler validates `values`, returns a
`ToolResult`, and an `IToolResultRenderer` draws the same text differently in the TUI. Full file:
[`UiShowcaseExtensionFactory.cs`](../../../samples/extensions/ui-showcase/UiShowcaseExtension/UiShowcaseExtensionFactory.cs).

The manifest declares the local name:

```json
{
    "id": "uikit",
    "tools": ["bars"]
}
```

## Declaring an output schema

When a tool declares `OutputSchema`, the handler can put a `JsonElement` in
`ToolResult.StructuredContent`. The text in `Output` remains the model-facing result; the
structured value travels beside it where the loop sends it. The `lsp-diagnostics` tool-result hook
attaches `{"path", "diagnostics": […]}` this way.

## See also

- Tool exposure and the core tool model: [`docs/spec/extensibility.md`](../../spec/extensibility.md#tool-model)
- Test a tool through the real harness: [testing.md](../testing.md)
- The approval and hook order: [hooks.md](hooks.md)
