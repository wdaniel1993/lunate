using System.Text.Json;
using Lunate.Extensibility;
using static Lunate.Coding.Tests.ExtensionsViewTestData;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>--json</c> contract of the extensions view (design D3/D5): the ordered array of the same
/// fields as the text, with <c>loadMs</c> and the <c>slow</c> marker only on loaded extensions.
/// </summary>
public sealed class ExtensionsViewJsonTests
{
    [Fact]
    public void Json_carries_the_ordered_fields_with_load_time_only_on_loaded_extensions()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("ext-two", ExtensionScope.Project, version: "0.2.0", services: ["greeter"]),
            Descriptor("ext-one"),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Denied("ext-two"), Loaded("ext-one", 12)];

        using JsonDocument document = JsonDocument.Parse(
            ExtensionsView.Json(descriptors, results, _ => new ExtensionCounts(2, 0, 1, 1))
        );

        JsonElement denied = document.RootElement[0];
        Assert.Equal("ext-two", denied.GetProperty("id").GetString());
        Assert.Equal("0.2.0", denied.GetProperty("version").GetString());
        Assert.Equal("project", denied.GetProperty("scope").GetString());
        Assert.Equal("trust-denied", denied.GetProperty("state").GetString());
        Assert.False(denied.TryGetProperty("loadMs", out _));
        Assert.False(denied.TryGetProperty("slow", out _));
        Assert.Equal(0, denied.GetProperty("tools").GetInt32());
        Assert.Equal(0, denied.GetProperty("commands").GetInt32());
        Assert.Equal(0, denied.GetProperty("hooks").GetInt32());
        Assert.Equal(1, denied.GetProperty("services").GetInt32());

        JsonElement loaded = document.RootElement[1];
        Assert.Equal("ext-one", loaded.GetProperty("id").GetString());
        Assert.Equal("0.1.0", loaded.GetProperty("version").GetString());
        Assert.Equal("global", loaded.GetProperty("scope").GetString());
        Assert.Equal("loaded", loaded.GetProperty("state").GetString());
        Assert.Equal(12, loaded.GetProperty("loadMs").GetInt64());
        Assert.False(loaded.TryGetProperty("slow", out _));
        Assert.Equal(2, loaded.GetProperty("tools").GetInt32());
        Assert.Equal(0, loaded.GetProperty("commands").GetInt32());
        Assert.Equal(1, loaded.GetProperty("hooks").GetInt32());
        Assert.Equal(1, loaded.GetProperty("services").GetInt32());
    }

    [Fact]
    public void Json_marks_a_slow_load()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-one")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one", milliseconds: 700)];

        using JsonDocument document = JsonDocument.Parse(
            ExtensionsView.Json(descriptors, results, NoCounts)
        );

        JsonElement node = document.RootElement[0];
        Assert.Equal(700, node.GetProperty("loadMs").GetInt64());
        Assert.True(node.GetProperty("slow").GetBoolean());
    }
}
