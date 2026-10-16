using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>The skill-source registry: registration, replace-with-warning and per-extension unload.</summary>
public sealed class SkillSourceRegistryTests
{
    private sealed class Source(string id, params string[] directories) : ISkillSource
    {
        public string Id => id;

        public IReadOnlyList<string> SkillDirectories => directories;
    }

    [Fact]
    public void Registers_and_lists_sources()
    {
        var registry = new SkillSourceRegistry();

        registry.Register("hello", new Source("main", "/one", "/two"));

        RegisteredSkillSource registered = Assert.Single(registry.List());
        Assert.Equal("hello", registered.ExtensionId);
        Assert.Equal("main", registered.Source.Id);
        Assert.Equal(["/one", "/two"], registered.Source.SkillDirectories);
    }

    [Fact]
    public void A_duplicate_id_replaces_the_earlier_source_with_a_logged_warning()
    {
        var log = new RecordingExtensionLog();
        var registry = new SkillSourceRegistry(log);

        registry.Register("first", new Source("main", "/one"));
        registry.Register("second", new Source("main", "/two"));

        RegisteredSkillSource registered = Assert.Single(registry.List());
        Assert.Equal("second", registered.ExtensionId);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains(
                    "replaces the source from extension 'first'",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void Unregister_removes_only_that_extensions_sources()
    {
        var registry = new SkillSourceRegistry();
        registry.Register("first", new Source("a", "/one"));
        registry.Register("second", new Source("b", "/two"));

        registry.Unregister("first");

        RegisteredSkillSource registered = Assert.Single(registry.List());
        Assert.Equal("second", registered.ExtensionId);
    }

    [Fact]
    public void A_blank_source_id_is_refused()
    {
        var registry = new SkillSourceRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register("hello", new Source(" ")));
    }
}
