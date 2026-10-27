# Hooks

A hook handler observes or changes one point in a run. Implement one or more handler interfaces
from `Lunate.Extensibility.Abstractions` and register each instance with `context.Register`:

```csharp
context.Register(new PermissionGateToolCallingHandler(context.Id, context.Settings, context.Log));
```

The full catalogue — when each hook fires, what it may change and its failure policy — is in
[`docs/spec/extensibility.md`](../../spec/extensibility.md#hook-catalogue). This page is the
author-facing index and the rules that are easy to get wrong.

## The API

Every handler derives from `IHookHandler` and has an `int Priority`. Higher values run first; equal
priorities keep extension load order, then registration order. All handlers are asynchronous and
receive a `CancellationToken`.

| Interface | Fires | Handler result |
| --- | --- | --- |
| `IProjectTrustHandler` | Before a project extension loads | `ProjectTrustResult` (`Allow` / `Deny`); only `Deny` acts — `Allow` is a veto that did not fire, and the prompt still runs |
| `ISessionStartedHandler` | Once after every extension loaded at session start | none |
| `ISessionEndingHandler` | At session end | none |
| `IInputReceivedHandler` | Raw user input, before anything queues | `InputReceivedResult` (`PassThrough` / `Transform` / `Consume`) |
| `IRunStartingHandler` | Before a run's first model call | `RunStartingResult` (`None` / `Apply`) |
| `IContextBuildingHandler` | Before every model request | `ContextBuildingResult` |
| `IProviderStreamEventHandler` | Every raw provider update | none (observe only) |
| `IMessageCompletedHandler` | The final assistant message of a model call | `MessageCompletedResult` (`Keep` / `Replace`) |
| `IToolCallingHandler` | Before approval and execution | `ToolCallingResult` (`Proceed` / `Block`) |
| `IToolResultReadyHandler` | After a tool executed | `ToolResultReadyResult` |
| `ITurnEndedHandler` | A turn boundary | `TurnEndedResult` (`None` / `TurnEnded`) |
| `IRunSettledHandler` | The settled run | none |
| `ICompactingHandler` | Before compaction | `CompactingResult` (`UseDefault` / `Provide`) |
| `IModelChangedHandler` | After the active model changed | none |
| `IToolsChangedHandler` | After the tool set changed | none |

File changes are a subscription rather than `context.Register`; see
[file-changes.md](file-changes.md).

## Constraints

- **Timeout.** Each handler has 5 seconds (`HookRunnerOptions.HandlerTimeout`).
  `ProviderStreamEvent` has a 100 ms fast path (`ProviderStreamEventTimeout`) and must stay cheap.
- **Slow hooks.** The first dispatch above 500 ms (`SlowHookThreshold`) logs one warning naming the
  extension, hook, elapsed time and threshold; later crossings are counted. `/extensions` shows the
  slowest hook above the threshold as `hook <Hook> max <ms> ms, <slow>/<calls> slow`.
- **Context budget.** `ContextBuilding` additions and `RunStarting` section *additions* are
  counted per extension against `ContextBudgetPerExtension` (default 2000 estimated tokens, one
  token per four characters rounded up). An addition that would cross the budget is dropped and
  logged; edits to existing prompt sections are not counted.
- **Continuations.** A run grants at most three continuations (`MaxContinuations`), counted across
  its turn boundaries. `RunStarting` and `RunSettled` reset the count.
- **Failure policy.** A handler exception or timeout never reaches the run (caller cancellation
  still propagates). The hook applies its policy: `ToolCalling` blocks the call fail-safe,
  `ProjectTrust` denies, `Compacting` falls back to the default, and every other hook keeps the
  original value or skips that handler and continues.
- **Child runs.** Payloads implementing `IParentRunIdPayload` carry the child run's `ParentRunId`
  when the hook fired inside a subagent run.

## Result composition

- `InputReceived`: each handler sees the previous text. The first `Consume` stops the chain; the
  final `Transform` is the input.
- `RunStarting`: each handler sees the sections the previous handlers produced. A `PromptSectionEdit`
  with `Text: null` removes the section. The last `ActiveTools` selection wins.
- `ContextBuilding`: each handler sees the previous additions; all accepted messages are merged and
  source-tagged with the extension id.
- `MessageCompleted`: each handler sees the current text; the final replacement is kept with the
  original role.
- `ToolCalling`: each handler sees the current arguments. `Proceed(null)` leaves them unchanged;
  `Proceed(arguments)` passes the new value to the next handler and then to the approval prompt.
  The first `Block` wins, and nothing is approved or executed.
- `ToolResultReady`: each handler sees the previous output. The last non-null `Data` and
  `StructuredContent` win. `ToolResultReadyResult` can replace the output and attach
  model-facing structured content.
- `TurnEnded`: entries are appended to the session and stamped with the producing extension id.
  `RequestContinuation` asks for one more turn; several handlers asking still produce one
  continuation.
- `Compacting`: the last supplied summary wins; a handler failure falls back to the host default.

## Example: block before approval

The `permission-gate` handler blocks a destructive call and lets everything else through. It
reads the resolved annotations from the payload, never a hard-coded tool-name list.

```csharp
public sealed class PermissionGateToolCallingHandler(
    string extensionId,
    IExtensionSettings settings,
    IExtensionLog log
) : IToolCallingHandler
{
    public int Priority => 0;

    public ValueTask<ToolCallingResult> HandleAsync(
        ToolCallingPayload payload,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.Annotations.Contains("destructive", StringComparer.Ordinal))
        {
            return ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Proceed(null));
        }

        string reason = $"permission-gate: destructive tool '{payload.ToolName}' blocked";
        log.Warn($"permission-gate[{extensionId}]: blocked '{payload.ToolName}'");
        return ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Block(reason));
    }
}
```

Full file:
[`PermissionGateToolCallingHandler.cs`](../../../samples/extensions/permission-gate/PermissionGateExtension/PermissionGateToolCallingHandler.cs).
`ToolCalling` runs before the approval prompt, so the block happens without asking.

## Example: attach a result

The `lsp-diagnostics` handler is the reference for the transformation shape: after a successful
`write` or `edit` of a `.cs` file it resolves the path from `payload.Arguments`, asks its language
server, appends a labeled section to `payload.Output`, and returns it with the diagnostics as the
third argument of `ToolResultReadyResult` (`StructuredContent`). Every not-applicable call and
every failure returns the original output unchanged. Full file:
[`LspDiagnosticsToolResultReadyHandler.cs`](../../../samples/extensions/lsp-diagnostics/LspDiagnosticsExtension/LspDiagnosticsToolResultReadyHandler.cs).

## See also

- The hook catalogue and failure policies: [`docs/spec/extensibility.md`](../../spec/extensibility.md#hook-catalogue)
- Testing hooks through the real host: [testing.md](../testing.md)
- The `IFileChangedHandler` contract: [file-changes.md](file-changes.md)
