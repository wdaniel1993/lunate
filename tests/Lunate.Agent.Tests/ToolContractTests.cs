using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class ToolContractTests
{
    [Fact]
    public void Interface_defaults_are_pinned()
    {
        ITool tool = new ContractTool();

        Assert.Equal(ToolExposure.Direct, tool.Exposure);
        Assert.Null(tool.Namespace);
        Assert.Null(tool.Annotations);
        Assert.Null(tool.OutputSchema);
        Assert.Equal(ToolConcurrency.Parallel, tool.Concurrency);
        Assert.Equal(ToolRisk.Write, tool.Risk);
    }

    [Fact]
    public void Read_only_annotations_derive_read_only_risk()
    {
        ITool tool = new ContractTool { Annotations = new ToolAnnotations(ReadOnly: true) };

        Assert.Equal(ToolRisk.ReadOnly, tool.Risk);
    }

    [Fact]
    public void An_explicit_risk_wins_over_annotations()
    {
        var tool = new ScriptedTool(
            "bash",
            "Runs commands.",
            """{"type":"object"}""",
            ToolRisk.Execute
        )
        {
            Annotations = new ToolAnnotations(ReadOnly: true),
        };

        Assert.Equal(ToolRisk.Execute, tool.Risk);
    }

    [Fact]
    public void Annotation_and_namespace_defaults_are_pinned()
    {
        var annotations = new ToolAnnotations();
        var toolNamespace = new ToolNamespace("files");

        Assert.False(annotations.ReadOnly);
        Assert.False(annotations.Destructive);
        Assert.False(annotations.Idempotent);
        Assert.False(annotations.OpenWorld);
        Assert.Equal(
            ("files", null, null),
            (toolNamespace.Name, toolNamespace.Description, toolNamespace.Instructions)
        );
    }

    [Fact]
    public void Result_carries_structured_content_and_usage()
    {
        using JsonDocument document = JsonDocument.Parse("""{"count":2}""");
        var usage = new UsageDetails { InputTokenCount = 3, OutputTokenCount = 1 };

        var result = new ToolResult(
            "output",
            IsError: false,
            Details: "ui",
            StructuredContent: document.RootElement.Clone(),
            Usage: usage
        );

        Assert.Equal("output", result.Output);
        Assert.Equal("ui", result.Details);
        Assert.Equal(2, result.StructuredContent!.Value.GetProperty("count").GetInt32());
        Assert.Same(usage, result.Usage);
    }

    [Fact]
    public void Risk_values_are_the_approval_vocabulary()
    {
        Assert.Equal(
            [ToolRisk.ReadOnly, ToolRisk.Write, ToolRisk.Execute],
            Enum.GetValues<ToolRisk>()
        );
        Assert.Equal(0, (int)ToolRisk.ReadOnly);
        Assert.Equal(1, (int)ToolRisk.Write);
        Assert.Equal(2, (int)ToolRisk.Execute);
    }

    [Fact]
    public void Result_defaults_details_to_null()
    {
        var result = new ToolResult("output", IsError: false);

        Assert.Equal("output", result.Output);
        Assert.False(result.IsError);
        Assert.Null(result.Details);
    }

    [Fact]
    public void Result_carries_ui_only_details()
    {
        var details = new { Diff = "+ line" };
        var result = new ToolResult("edited", IsError: false, Details: details);

        Assert.Same(details, result.Details);
    }

    [Fact]
    public void Context_carries_working_directory_and_events()
    {
        var events = new RecordingAgentEvents();

        var context = new ToolContext("/repo", events);

        Assert.Equal("/repo", context.WorkingDirectory);
        Assert.Same(events, context.Events);
    }

    [Fact]
    public async Task A_hand_written_tool_runs_through_the_contract()
    {
        const string schema = """{"type":"object","properties":{"path":{"type":"string"}}}""";
        var tool = new ScriptedTool("read", "Reads a file.", schema);
        var events = new RecordingAgentEvents();
        var context = new ToolContext("/repo", events);

        using JsonDocument args = JsonDocument.Parse("""{"path":"a.txt"}""");
        ToolResult result = await tool.ExecuteAsync(
            args.RootElement,
            context,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("ran read", result.Output);
        Assert.False(result.IsError);
        Assert.Equal(ToolRisk.ReadOnly, tool.Risk);
        Assert.Equal(schema, tool.ParametersSchema.GetRawText());
        Assert.Equal("""{"path":"a.txt"}""", tool.ReceivedArgsRaw);
        Assert.Same(context, tool.ReceivedContext);
    }
}
