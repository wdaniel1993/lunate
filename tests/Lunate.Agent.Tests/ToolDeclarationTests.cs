using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class ToolDeclarationTests
{
    private const string SchemaJson =
        """{ "type" : "object", "properties" : { "b" : { "type" : "number" }, "a" : { "type" : "string" } }, "required" : ["b", "a"] }""";

    [Fact]
    public void Name_and_description_are_the_tools_own()
    {
        var tool = new ScriptedTool("read", "Reads a file.", SchemaJson);

        var declaration = new ToolDeclaration(tool);

        Assert.Equal("read", declaration.Name);
        Assert.Equal("Reads a file.", declaration.Description);
    }

    [Fact]
    public void Schema_reaches_the_model_byte_for_byte()
    {
        var tool = new ScriptedTool("read", "Reads a file.", SchemaJson);

        var declaration = new ToolDeclaration(tool);

        Assert.Equal(SchemaJson, declaration.JsonSchema.GetRawText());
        Assert.Equal(tool.ParametersSchema.GetRawText(), declaration.JsonSchema.GetRawText());
    }

    [Fact]
    public void Schema_survives_the_source_document_being_disposed()
    {
        var tool = new DocumentBackedTool(SchemaJson);

        var declaration = new ToolDeclaration(tool);
        tool.Dispose();

        Assert.Equal(SchemaJson, declaration.JsonSchema.GetRawText());
    }

    [Fact]
    public async Task Invoking_through_Microsoft_Extensions_AI_is_refused()
    {
        var tool = new ScriptedTool("read", "Reads a file.", SchemaJson);
        var declaration = new ToolDeclaration(tool);

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
            async () =>
            {
                await declaration.InvokeAsync(
                    new AIFunctionArguments { ["path"] = "a.txt" },
                    TestContext.Current.CancellationToken
                );
            }
        );

        Assert.Contains("ADR-0003", exception.Message);
    }

    [Fact]
    public void The_wrapped_tool_stays_reachable_for_the_loop()
    {
        var tool = new ScriptedTool("read", "Reads a file.", SchemaJson);

        var declaration = new ToolDeclaration(tool);

        Assert.Same(tool, declaration.Tool);
    }

    private sealed class DocumentBackedTool(string schemaJson) : ITool, IDisposable
    {
        private readonly JsonDocument _document = JsonDocument.Parse(schemaJson);

        public string Name => "read";

        public string Description => "Reads a file.";

        public JsonElement ParametersSchema => _document.RootElement;

        public ToolRisk Risk => ToolRisk.ReadOnly;

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("ok", IsError: false));

        public void Dispose() => _document.Dispose();
    }
}
