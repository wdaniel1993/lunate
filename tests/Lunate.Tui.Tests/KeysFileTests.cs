using System.Text.Json;

namespace Lunate.Tui.Tests;

/// <summary>
/// <c>keys.json</c> is loaded at run time, so its shape is the contract: it is the only place a
/// key's meaning is written down, and <c>docs/guide.md</c>'s table is generated from its
/// <c>guideRows</c> by <c>scripts/docs-sync</c>. These tests hold the file together on its own terms
/// — every binding well formed and documented once, every guide row's key cell the rendering of the
/// bindings it names, and every row that documents a key nothing routes stating why. The document's
/// copy of the table is checked by <c>scripts/docs-sync --check</c>, so no test reads <c>docs/</c>.
/// </summary>
public sealed class KeysFileTests
{
    private static readonly JsonElement Root = Resource();

    [Fact]
    public void The_keymap_is_embedded()
    {
        Assert.True(
            Root.TryGetProperty("bindings", out JsonElement bindings)
                && bindings.ValueKind == JsonValueKind.Array,
            $"{KeyBindingCatalog.ResourceName} has no 'bindings' array"
        );
        Assert.Equal(1, Root.GetProperty("version").GetInt32());
    }

    [Fact]
    public void Every_binding_is_well_formed()
    {
        foreach (JsonElement binding in Bindings())
        {
            string id = Text(binding, "id");
            Assert.False(string.IsNullOrWhiteSpace(id), "a binding has no id");
            Assert.False(
                string.IsNullOrWhiteSpace(Text(binding, "description")),
                $"binding '{id}' has no description"
            );
            Assert.True(
                Enum.TryParse(Text(binding, "intent"), ignoreCase: false, out RoutedKey parsed)
                    && Enum.IsDefined(parsed),
                $"binding '{id}' names an intent that is not a RoutedKey member"
            );
            Assert.True(
                Enum.TryParse(Text(binding, "context"), ignoreCase: false, out KeyContext _),
                $"binding '{id}' names a context that is not a KeyContext member"
            );
            JsonElement keys = binding.GetProperty("keys");
            Assert.True(keys.GetArrayLength() > 0, $"binding '{id}' lists no key patterns");
            Assert.All(
                keys.EnumerateArray(),
                key =>
                    Assert.True(
                        Enum.TryParse(Text(key, "kind"), ignoreCase: false, out KeyKind _),
                        $"binding '{id}' names a kind that is not a KeyKind member"
                    )
            );
        }
    }

    [Fact]
    public void No_two_bindings_share_an_id()
    {
        var ids = Bindings().Select(binding => Text(binding, "id")).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The loader reads every pattern the file declares: a dropped row is a dead key.</summary>
    [Fact]
    public void The_catalog_rows_are_the_patterns_the_file_declares()
    {
        int declared = Bindings().Sum(binding => binding.GetProperty("keys").GetArrayLength());

        Assert.Equal(declared, KeyBindingCatalog.All.Count);
        Assert.True(declared > 0, "keys.json declares no key patterns at all");
    }

    [Fact]
    public void Every_binding_is_documented_by_exactly_one_guide_row()
    {
        var documented = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonElement row in GuideRows())
        {
            foreach (string id in Texts(row, "bindings"))
            {
                Assert.False(
                    documented.TryGetValue(id, out string? other),
                    $"binding '{id}' is documented twice: rows '{other}' and '{Text(row, "id")}'"
                );
                documented[id] = Text(row, "id");
            }
        }

        string[] undocumented =
        [
            .. Bindings()
                .Select(binding => Text(binding, "id"))
                .Where(id => !documented.ContainsKey(id))
                .Select(id =>
                    $"binding '{id}' has no guide row in {KeyBindingCatalog.ResourceName}"
                ),
        ];
        Assert.Empty(undocumented);
    }

    /// <summary>
    /// A row's key cell is its bindings' keys, in the row's order, spelled the way the help overlay
    /// spells them and merged when two bindings share one. That is what makes the generated table and
    /// the overlay the same legend.
    /// </summary>
    [Fact]
    public void A_guide_rows_key_cell_is_the_rendering_of_its_bindings()
    {
        var byId = Bindings().ToDictionary(binding => Text(binding, "id"), StringComparer.Ordinal);
        foreach (JsonElement row in GuideRows())
        {
            string[] ids = [.. Texts(row, "bindings")];
            if (ids.Length == 0)
            {
                continue;
            }

            List<string> expected = [];
            foreach (string id in ids)
            {
                Assert.True(
                    byId.ContainsKey(id),
                    $"guide row '{Text(row, "id")}' names '{id}', which does not exist"
                );
                string rendered = KeyPatternFormatter.Describe(
                    Pattern(byId[id].GetProperty("keys")[0])
                );
                if (!expected.Contains(rendered, StringComparer.Ordinal))
                {
                    expected.Add(rendered);
                }
            }

            Assert.Equal(expected, Texts(row, "keys"));
        }
    }

    /// <summary>
    /// A row without bindings documents a key nothing routes. Like the exemption table it replaced,
    /// it must carry the reason it is allowed — adding one is a spec change, not a test edit.
    /// </summary>
    [Fact]
    public void A_row_with_no_bindings_states_why()
    {
        var spellings = KeyBindingCatalog
            .All.Select(binding => KeyPatternFormatter.Describe(binding.Key))
            .ToHashSet(StringComparer.Ordinal);
        foreach (JsonElement row in GuideRows())
        {
            string rowId = Text(row, "id");
            string[] keys = [.. Texts(row, "keys")];
            Assert.True(keys.Length > 0, $"guide row '{rowId}' documents no keys");
            Assert.False(
                string.IsNullOrWhiteSpace(Text(row, "action")),
                $"guide row '{rowId}' has no action text"
            );
            if (Texts(row, "bindings").Any())
            {
                continue;
            }

            Assert.False(
                string.IsNullOrWhiteSpace(Text(row, "reason")),
                $"guide row '{rowId}' documents keys no binding has, so it needs a 'reason'"
            );
            Assert.All(
                keys,
                key =>
                    Assert.False(
                        spellings.Contains(key),
                        $"guide row '{rowId}' calls '{key}' unbound, but a binding has it"
                    )
            );
        }
    }

    private static KeyPattern Pattern(JsonElement key) =>
        new(
            Enum.Parse<KeyKind>(Text(key, "kind")),
            Modifier(key, "ctrl"),
            Modifier(key, "alt"),
            Modifier(key, "shift"),
            key.TryGetProperty("text", out JsonElement text) ? text.GetString() : null
        );

    private static bool? Modifier(JsonElement key, string name) =>
        key.TryGetProperty(name, out JsonElement value) ? value.GetBoolean() : null;

    private static IEnumerable<JsonElement> Bindings() =>
        Root.GetProperty("bindings").EnumerateArray();

    private static IEnumerable<JsonElement> GuideRows() =>
        Root.TryGetProperty("guideRows", out JsonElement rows)
            ? rows.EnumerateArray()
            : throw new InvalidOperationException(
                $"{KeyBindingCatalog.ResourceName} has no 'guideRows' array"
            );

    private static IEnumerable<string> Texts(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement array)
            ? array.EnumerateArray().Select(value => value.GetString() ?? string.Empty)
            : [];

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static JsonElement Resource()
    {
        using Stream stream =
            typeof(KeyBindingCatalog).Assembly.GetManifestResourceStream(
                KeyBindingCatalog.ResourceName
            )
            ?? throw new InvalidOperationException(
                $"{KeyBindingCatalog.ResourceName} is not embedded in Lunate.Tui"
            );
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        Assert.False(string.IsNullOrWhiteSpace(json), "the keymap resource is empty");
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
