using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class ToolRegistryTests
{
    private const string Schema = """{"type":"object"}""";

    [Fact]
    public void Add_then_Find_returns_the_tool()
    {
        var registry = new ToolRegistry();
        var read = new ScriptedTool("read", "Reads a file.", Schema);

        registry.Add(read);

        Assert.Same(read, registry.Find("read"));
    }

    [Fact]
    public void Find_returns_null_for_an_unknown_name()
    {
        var registry = new ToolRegistry();
        registry.Add(new ScriptedTool("read", "Reads a file.", Schema));

        Assert.Null(registry.Find("missing"));
    }

    [Fact]
    public void Add_rejects_a_duplicate_name_immediately()
    {
        var registry = new ToolRegistry();
        var read = new ScriptedTool("read", "Reads a file.", Schema);
        registry.Add(read);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            registry.Add(new ScriptedTool("read", "Another read.", Schema))
        );

        Assert.Contains("read", exception.Message);
        Assert.Same(read, Assert.Single(registry.Tools));
    }

    [Fact]
    public void Add_rejects_null()
    {
        var registry = new ToolRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
    }

    [Fact]
    public void Tools_and_declarations_keep_registration_order()
    {
        var registry = new ToolRegistry();
        var read = new ScriptedTool("read", "Reads a file.", Schema);
        var write = new ScriptedTool("write", "Writes a file.", Schema);

        registry.Add(read);
        registry.Add(write);

        Assert.Collection(
            registry.Tools,
            tool => Assert.Same(read, tool),
            tool => Assert.Same(write, tool)
        );
        Assert.Collection(
            registry.Declarations,
            declaration => Assert.Equal("read", declaration.Name),
            declaration => Assert.Equal("write", declaration.Name)
        );
    }

    [Fact]
    public void Declarations_expose_only_direct_and_model_only_tools()
    {
        var registry = new ToolRegistry();
        registry.Add(new ContractTool("direct"));
        registry.Add(new ContractTool("model_only") { Exposure = ToolExposure.ModelOnly });
        registry.Add(new ContractTool("programmatic") { Exposure = ToolExposure.Programmatic });
        registry.Add(new ContractTool("deferred") { Exposure = ToolExposure.Deferred });
        registry.Add(new ContractTool("hidden") { Exposure = ToolExposure.Hidden });

        Assert.Equal(5, registry.Tools.Count);
        Assert.Equal(["direct", "model_only"], registry.Declarations.Select(d => d.Name));
    }

    [Fact]
    public void Declarations_are_adapters_over_the_registered_tools()
    {
        var registry = new ToolRegistry();
        var read = new ScriptedTool("read", "Reads a file.", Schema);
        registry.Add(read);

        AIFunction declaration = Assert.Single(registry.Declarations);

        Assert.Equal(read.ParametersSchema.GetRawText(), declaration.JsonSchema.GetRawText());
    }
}
