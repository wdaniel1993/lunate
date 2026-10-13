using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace CodeModeExtension.Tests;

internal static class TestSupport
{
    /// <summary>The extension directory the test build copies under the output root.</summary>
    public static string ExtensionDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "CodeModeExtension");

    /// <summary>
    /// Creates a host over the built sample extension; the settings file is written before the host
    /// loads the extension (missing keys keep their defaults).
    /// </summary>
    public static ExtensionTestHost CreateHost(
        TempDirectory temp,
        IToolApprover? approver = null,
        string? runtime = null,
        int? timeoutSeconds = null,
        int? maxOutputBytes = null,
        int? maxToolCalls = null
    )
    {
        var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "codemode",
                ExtensionDirectory = ExtensionDirectory,
                Approver = approver,
            }
        );
        WriteSettings(host, runtime, timeoutSeconds, maxOutputBytes, maxToolCalls);
        return host;
    }

    public static void WriteSettings(
        ExtensionTestHost host,
        string? runtime = null,
        int? timeoutSeconds = null,
        int? maxOutputBytes = null,
        int? maxToolCalls = null
    )
    {
        var values = new Dictionary<string, object>();
        if (runtime is { Length: > 0 })
        {
            values["runtime"] = runtime;
        }

        if (timeoutSeconds is { } timeout)
        {
            values["timeoutSeconds"] = timeout;
        }

        if (maxOutputBytes is { } bytes)
        {
            values["maxOutputBytes"] = bytes;
        }

        if (maxToolCalls is { } calls)
        {
            values["maxToolCalls"] = calls;
        }

        if (values.Count == 0)
        {
            return;
        }

        string directory = Path.Combine(host.StorePath, "extensions-settings");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "codemode.json"),
            JsonSerializer.Serialize(values)
        );
    }

    /// <summary>The registered <c>codemode__run</c> adapter, for direct calls (structured results).</summary>
    public static ExtensionToolAdapter Tool(ExtensionTestHost host) =>
        host.Runner.Tools.List().Single(tool => tool.Name == "codemode__run");

    /// <summary>Builds the model arguments for one run call.</summary>
    public static JsonElement CodeArgs(string code, int? timeoutSeconds = null)
    {
        var arguments = new Dictionary<string, object?> { ["code"] = code };
        if (timeoutSeconds is { } timeout)
        {
            arguments["timeoutSeconds"] = timeout;
        }

        return JsonSerializer.SerializeToElement(arguments);
    }

    /// <summary>The model-facing result the last request carried for the given tool call.</summary>
    public static string LastToolResult(ScriptedModelClient client, int requestIndex) =>
        Assert
            .Single(client.Requests[requestIndex][^1].Contents.OfType<FunctionResultContent>())
            .Result!.ToString()!;
}

/// <summary>
/// A tool the scripts call through the bridge. Programmatic isolation, destructive risk and
/// parameterized output are all expressed through the constructor.
/// </summary>
internal sealed class FixtureTool : ITool
{
    private readonly Func<JsonElement, string> _respond;

    public FixtureTool(
        string name,
        string? output = null,
        ToolExposure exposure = ToolExposure.Direct,
        bool destructive = false
    )
    {
        Name = name;
        _respond = _ => output ?? $"{name}-output";
        Exposure = exposure;
        Annotations = destructive ? new ToolAnnotations(ReadOnly: false, Destructive: true) : null;
    }

    public string Name { get; }

    public string Description => $"The {Name} fixture.";

    public JsonElement ParametersSchema { get; } =
        JsonSerializer.SerializeToElement(
            new { type = "object", properties = new { value = new { type = "string" } } }
        );

    public ToolExposure Exposure { get; }

    public ToolAnnotations? Annotations { get; }

    public int Calls { get; private set; }

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        Calls++;
        string text = _respond(args);
        return Task.FromResult(new ToolResult(text, IsError: false));
    }
}

/// <summary>A programmatic fixture that echoes its <c>value</c> argument.</summary>
internal sealed class EchoFixtureTool : ITool
{
    public string Name => "prog-fixture";

    public string Description => "Echoes the value argument.";

    public JsonElement ParametersSchema { get; } =
        JsonSerializer.SerializeToElement(
            new { type = "object", properties = new { value = new { type = "string" } } }
        );

    public ToolExposure Exposure => ToolExposure.Programmatic;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        string value =
            args.TryGetProperty("value", out JsonElement element)
            && element.ValueKind == JsonValueKind.String
                ? element.GetString()!
                : "none";
        return Task.FromResult(new ToolResult($"prog:{value}", IsError: false));
    }
}

/// <summary>
/// Mirrors the interactive session's "always" semantics: names granted "always" are auto-approved
/// silently; every other ask is decided by the queue and recorded. Exhaustion throws.
/// </summary>
internal sealed class AlwaysApprover(params string[] alwaysGranted) : IToolApprover
{
    private readonly HashSet<string> _always = new(alwaysGranted, StringComparer.Ordinal);
    private readonly Queue<bool> _decisions = new();

    public List<ApprovalRequest> Requests { get; } = [];

    public AlwaysApprover Decide(bool decision)
    {
        _decisions.Enqueue(decision);
        return this;
    }

    public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        if (_always.Contains(request.Tool.Name))
        {
            return ValueTask.FromResult(true);
        }

        Requests.Add(request);
        if (!_decisions.TryDequeue(out bool decision))
        {
            throw new InvalidOperationException(
                $"AlwaysApprover has no decision left for '{request.Tool.Name}'."
            );
        }

        return ValueTask.FromResult(decision);
    }
}

/// <summary>
/// An approver that leaves asks for the named tool pending until released or cancelled, so tests
/// can observe a bridged call that is still awaiting approval (for example when a script timeout
/// fires). Every other tool is approved silently.
/// </summary>
internal sealed class BlockingApprover(string blockedTool) : IToolApprover
{
    private readonly TaskCompletionSource<bool> _decision = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public TaskCompletionSource<bool> Asked { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.Tool.Name, blockedTool, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(true);
        }

        Asked.TrySetResult(true);
        return new ValueTask<bool>(_decision.Task.WaitAsync(ct));
    }

    public void Release(bool approved) => _decision.TrySetResult(approved);
}
