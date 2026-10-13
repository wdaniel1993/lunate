using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Testing;

namespace SubagentsExtension.Tests;

internal static class TestSupport
{
    /// <summary>The extension directory the test build copies under the output root.</summary>
    public static string ExtensionDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "SubagentsExtension");

    /// <summary>
    /// Creates a host over the built sample extension; the settings file is written before the host
    /// loads the extension (missing keys stay unset).
    /// </summary>
    public static ExtensionTestHost CreateHost(
        TempDirectory temp,
        IToolApprover? approver = null,
        SubagentLimits? limits = null,
        Func<AgentHarnessOptions, AgentHarnessOptions>? configure = null,
        int? defaultMaxSteps = null,
        string? description = null
    )
    {
        var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "subagents",
                ExtensionDirectory = ExtensionDirectory,
                Approver = approver,
                SubagentLimits = limits,
                ConfigureHarness = configure,
            }
        );
        WriteSettings(host, defaultMaxSteps, description);
        return host;
    }

    public static void WriteSettings(
        ExtensionTestHost host,
        int? defaultMaxSteps = null,
        string? description = null
    )
    {
        if (defaultMaxSteps is null && description is null)
        {
            return;
        }

        string directory = Path.Combine(host.StorePath, "extensions-settings");
        Directory.CreateDirectory(directory);
        var values = new Dictionary<string, object>();
        if (defaultMaxSteps is { } steps)
        {
            values["defaultMaxSteps"] = steps;
        }

        if (description is { } text)
        {
            values["description"] = text;
        }

        File.WriteAllText(
            Path.Combine(directory, "subagents.json"),
            JsonSerializer.Serialize(values)
        );
    }

    public static string ChildSessionPath(string parentSessionPath, string childSessionId) =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(parentSessionPath))!,
            SessionPaths.SessionFileName(childSessionId)
        );
}

/// <summary>Records every approval ask with its asking-run identity; decisions by tool name.</summary>
internal sealed class RecordingApprover(Func<string, bool> decide) : IToolApprover
{
    public List<ApprovalRequest> Requests { get; } = [];

    public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return ValueTask.FromResult(decide(request.Tool.Name));
    }
}

/// <summary>A write-risk tool the child runs in tests (the harness's registry holds only composed tools).</summary>
internal sealed class TestProbeTool : ITool
{
    public string Name => "child-probe";

    public string Description => "A write-risk probe the child can call.";

    public JsonElement ParametersSchema { get; } =
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });

    public ToolRisk Risk => ToolRisk.Write;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct) =>
        Task.FromResult(new ToolResult("probe ran", IsError: false));
}

/// <summary>Records the ToolCalling hook contexts (the harness seam).</summary>
internal sealed class RecordingHooks : IAgentHookPoints
{
    public List<AgentToolCallingContext> ToolCallings { get; } = [];

    public ValueTask<AgentToolCallingResult> ToolCallingAsync(
        AgentToolCallingContext context,
        CancellationToken cancellationToken
    )
    {
        ToolCallings.Add(context);
        return ValueTask.FromResult<AgentToolCallingResult>(
            new AgentToolCallingResult.Proceed(context.Arguments)
        );
    }
}
