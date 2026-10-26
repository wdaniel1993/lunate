using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Coding.Tests;

/// <summary>
/// The one serializer the MCP config hash and the trust-inventory digest go through
/// (repo-quality spec: "the serializer never follows the platform"). With indentation off it emits
/// no newline at all, so the pin is invisible today — and it is what keeps both digests identical
/// on a Windows and a POSIX machine the moment a document is written indented.
/// </summary>
public sealed class CanonicalJsonTests
{
    [Fact]
    public void The_serializer_behind_both_hashes_is_pinned_to_LF()
    {
        Assert.Equal("\n", CanonicalJson.Options.NewLine);

        var indented = new JsonSerializerOptions(CanonicalJson.Options) { WriteIndented = true };
        string text = new JsonObject
        {
            ["a"] = 1,
            ["b"] = new JsonObject { ["c"] = 2 },
        }.ToJsonString(indented);

        Assert.Contains('\n', text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void Canonical_text_carries_no_carriage_return_and_does_not_change_between_serializations()
    {
        var node = new JsonObject
        {
            ["kind"] = "extension",
            ["id"] = "hello",
            ["filesChanged"] = false,
            ["hash"] = "a-hash",
        };

        string first = CanonicalJson.Text(node);

        Assert.DoesNotContain('\r', first);
        Assert.Equal(first, CanonicalJson.Text(node));
        Assert.Equal(CanonicalJson.Hash(node), CanonicalJson.Hash(JsonNode.Parse(first)!));
    }
}
