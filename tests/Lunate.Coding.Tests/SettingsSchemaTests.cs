using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>settings-schema.json</c> is the one place every accepted settings key is written down; the
/// parser accepts its keys through literals in a dozen <c>SettingsStore*.cs</c> partials, and the
/// generated docs/configuration.md table is rendered from this file by <c>scripts/docs-sync</c>.
/// These tests pin the three sides together so a key cannot exist in one of them alone: the file
/// must list what the store accepts (both directions), a file carrying every listed key must
/// resolve cleanly, and the file's scope column must match what the project merge actually does.
/// </summary>
public sealed partial class SettingsSchemaTests
{
    private const string WildcardId = "probe";
    private const string ProbeModel = "probe-model";
    private const string ProbeProvider = "probe-provider";

    /// <summary>
    /// One valid value per schema key, so one settings file can carry everything the schema
    /// lists. Values are valid by construction: the cross-field rules around them (both
    /// decision profiles exist, the provider endpoint loads, usage targets resolve) are in the
    /// fixtures the sink builders assemble beside the probes.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, object?> Probes = new Dictionary<
        string,
        object?
    >
    {
        ["schemaVersion"] = 2,
        ["model"] = ProbeModel,
        ["approval"] = "auto-edit",
        ["verify"] = "off",
        ["toolDetails"] = "expanded",
        ["startupHeader"] = false,
        ["bashGuard"] = false,
        ["updateCheck"] = false,
        ["thinking"] = "low",
        ["thinkingFull"] = true,
        ["favorites"] = new[] { ProbeModel },
        ["tools.extra"] = new[] { "grep" },
        ["output.toolResultLimit"] = 100,
        ["output.spillThresholdTokens"] = 50,
        ["projection.batchTurns"] = 2,
        ["projection.maskTurns"] = 4,
        ["projection.maskErrorTurns"] = 8,
        ["subagents.concurrency"] = 1,
        ["subagents.tokenBudget"] = 100,
        ["subagents.treeTokenBudget"] = 100,
        ["roslyn.maxLoadedWorkspaces"] = 1,
        ["tui.fullscreen"] = true,
        ["tui.theme"] = "mono",
        ["tui.keys.*"] = new[] { ProbeKeySpec() },
        ["decisions.profile"] = WildcardId,
        ["decisions.approvals"] = "off",
        ["decisions.approvalsThreshold"] = 0.5,
        ["decisions.routing"] = "off",
        ["decisions.routingProfiles"] = new[] { WildcardId, "probe-two" },
        ["decisions.tool"] = "off",
        ["decisions.questions"] = "off",
        ["providers.*.baseUrl"] = "https://probe.example.test/v1",
        ["providers.*.model"] = ProbeModel,
        ["providers.*.catalogName"] = "Probe",
        ["profiles.*.provider"] = ProbeProvider,
        ["profiles.*.model"] = ProbeModel,
        ["profiles.*.reasoningLevel"] = "low",
        ["profiles.*.reasoningBudget"] = 1024,
        ["profiles.*.maxOutput"] = 1024,
        ["profiles.*.temperature"] = 0.5,
        ["profiles.*.contextWindow"] = 1024,
        ["profiles.*.capabilities.apis"] = new[] { "api-probe" },
        ["profiles.*.capabilities.inputModalities"] = new[] { "text" },
        ["profiles.*.capabilities.outputModalities"] = new[] { "text" },
        ["profiles.*.capabilities.features"] = new[] { "feature-probe" },
        ["usages.chat"] = WildcardId,
        ["usages.fast"] = "probe-two",
        ["usages.subagents"] = WildcardId,
        ["usages.compaction"] = "probe-two",
    };

    [Fact]
    public void Every_schema_entry_has_a_probe_value()
    {
        string[] missing =
        [
            .. SettingsSchema
                .All.Select(entry => entry.Name)
                .Where(name => !Probes.ContainsKey(name)),
        ];
        Assert.True(
            missing.Length == 0,
            $"settings-schema.json lists {string.Join(", ", missing)} with no probe value; add "
                + "each to SettingsSchemaTests.Probes so the agreement test can carry the key"
        );
    }

    [Fact]
    public void A_settings_file_carrying_every_schema_key_resolves_without_problems()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(path, SinkJson());

        AgentSettings settings = SettingsStore.Resolve(path, _ => null);

        // Values are asserted where silent dropping has been the failure mode before: a key the
        // parser accepted but a resolution step ignored produced no problem at all, so a
        // parse-only assertion would pass while the setting did nothing.
        Assert.Equal(100, settings.ToolOutputLimit);
        Assert.Equal(50, settings.SpillThresholdTokens);
        Assert.Equal(1, settings.Subagents.Concurrency);
        Assert.Equal(1, settings.Roslyn.MaxLoadedWorkspaces);
        Assert.Equal(ProbeProvider, settings.Profiles[WildcardId].Provider);
        Assert.Equal(1024, settings.Profiles[WildcardId].MaxOutput);
        Assert.Equal(0.5, settings.Decisions.ApprovalsThreshold);
    }

    [Fact]
    public void The_schema_lists_exactly_the_literals_the_store_accepts()
    {
        IReadOnlySet<string> accepted = AcceptedKeys();
        IReadOnlySet<string> documented = SchemaSegments();

        string[] undocumented =
        [
            .. accepted.Where(key => !documented.Contains(key, StringComparer.Ordinal)),
        ];
        Assert.True(
            undocumented.Length == 0,
            string.Join(", ", undocumented)
                + " is accepted by SettingsStore but missing from settings-schema.json; add an "
                + "entry for it (or, if the literal is a value literal or a refused name, keep it "
                + "out of the captured set)"
        );

        string[] phantom =
        [
            .. documented.Where(key => !accepted.Contains(key, StringComparer.Ordinal)),
        ];
        Assert.True(
            phantom.Length == 0,
            string.Join(", ", phantom)
                + " is listed in settings-schema.json but no SettingsStore switch or comparison "
                + "accepts it; drop the entry or fix the name"
        );
    }

    /// <summary>
    /// The schema ties the allowed-value lists of keys whose literals live in other assemblies'
    /// tables (the TUI theme, the optional tools) to those tables, so the reference cannot name
    /// a value the parser refuses.
    /// </summary>
    [Fact]
    public void The_schema_allowed_values_match_the_tables_the_parsers_use()
    {
        Assert.Equal(["default", "ansi16", "mono"], Entry("tui.theme").Allowed);
        foreach (string theme in Entry("tui.theme").Allowed)
        {
            Assert.True(
                TuiTheme.TryGet(theme, out _),
                $"the schema lists '{theme}' as an accepted tui.theme; the TUI refuses it"
            );
        }

        Assert.Equal((IReadOnlyList<string>)OptionalTools.Names, Entry("tools.extra").Allowed);
    }

    [Fact]
    public void The_schema_entries_are_sorted_by_name()
    {
        string[] names = [.. SettingsSchema.All.Select(entry => entry.Name)];
        string[] sorted = [.. names.Order(StringComparer.Ordinal)];
        Assert.True(
            names.SequenceEqual(sorted),
            "settings-schema.json must list its entries sorted by name so the generated table "
                + "diffs stay deterministic; sort the file and run scripts/docs-sync"
        );
    }

    [Fact]
    public void The_schema_marks_every_scope_as_either_user_or_project()
    {
        Assert.All(
            SettingsSchema.All,
            entry =>
                Assert.True(
                    string.Equals(entry.Scope, "user", StringComparison.Ordinal)
                        || string.Equals(entry.Scope, "user, project", StringComparison.Ordinal),
                    $"{entry.Name} declares scope '{entry.Scope}'; a settings key is either 'user' "
                        + "or 'user, project'"
                )
        );
    }

    [Fact]
    public void Project_settings_may_carry_every_key_the_schema_marks_project_scoped()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("user-settings.json"), """{ "schemaVersion": 2 }""");
        File.WriteAllText(temp.File("project-settings.json"), ProjectSinkJson());

        AgentSettings settings = SettingsStore.ResolveWithProject(
            temp.File("user-settings.json"),
            temp.File("project-settings.json"),
            [SettingsProvider(ProbeProvider)],
            _ => null
        );

        Assert.Equal(ProbeProvider, settings.Profiles[WildcardId].Provider);
        Assert.Equal(1024, settings.Profiles[WildcardId].MaxOutput);
        Assert.Equal(ProbeProvider, settings.Profiles["probe-two"].Provider);
        Assert.Equal(WildcardId, settings.Usages["chat"]);
        Assert.Equal(1, settings.Subagents.Concurrency);
        Assert.Equal(100, settings.Subagents.TokenBudget);
        Assert.Equal(100, settings.ToolOutputLimit);
        Assert.Equal(50, settings.SpillThresholdTokens);
    }

    [Fact]
    public void A_user_only_key_is_refused_in_project_settings()
    {
        foreach (
            SettingEntry entry in SettingsSchema.All.Where(candidate => candidate.Scope == "user")
        )
        {
            using var temp = new TempDirectory();
            File.WriteAllText(temp.File("user-settings.json"), """{ "schemaVersion": 2 }""");
            File.WriteAllText(temp.File("project-settings.json"), ScopedProbeJson(entry.Name));

            InvalidDataException exception = Assert.ThrowsAny<InvalidDataException>(() =>
                SettingsStore.ResolveWithProject(
                    temp.File("user-settings.json"),
                    temp.File("project-settings.json"),
                    [SettingsProvider(ProbeProvider)],
                    _ => null
                )
            );

            // The store refuses user-only keys in three places: the scope scan ('is not
            // allowed'), the provider-definition scan ('forbidden; providers are user-scoped'),
            // and the merge ('capabilities is user-only'). Any of them proves the key cannot
            // define itself from a project file, and naming which one keeps the probe honest.
            string expected = entry.Name.StartsWith(
                "profiles.*.capabilities",
                StringComparison.Ordinal
            )
                ? "capabilities is user-only"
                : "not allowed|forbidden";
            Assert.True(
                Regex.IsMatch(exception.Message, expected),
                $"{entry.Name} is marked 'user' in the schema, but its project probe failed with "
                    + $"a different refusal: {exception.Message}"
            );
        }
    }

    [Fact]
    public void Capabilities_stay_user_scoped_when_a_profile_is_project_defined()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("user-settings.json"), """{ "schemaVersion": 2 }""");
        File.WriteAllText(
            temp.File("project-settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "probe": {
                  "provider": "probe-provider",
                  "model": "probe-model",
                  "capabilities": { "apis": ["api-probe"] }
                }
              }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.ResolveWithProject(
                temp.File("user-settings.json"),
                temp.File("project-settings.json"),
                [SettingsProvider(ProbeProvider)],
                _ => null
            )
        );

        Assert.Contains("capabilities is user-only", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the user settings file that carries one valid value per schema entry, plus the
    /// second profile the routing-profiles fixture needs.
    /// </summary>
    private static string SinkJson()
    {
        JsonObject root = new();
        foreach ((string name, object? value) in Probes)
        {
            SetPath(root, name, ProbeIds(name), value);
        }

        root["profiles"]!["probe-two"] = new JsonObject
        {
            ["provider"] = ProbeProvider,
            ["model"] = ProbeModel,
        };
        return root.ToJsonString() + "\n";
    }

    /// <summary>
    /// Builds the project settings file that carries one valid value per entry the schema marks
    /// <c>user, project</c>. Project profiles cannot define a provider, so the fixture passes
    /// the user provider the probes reference.
    /// </summary>
    private static string ProjectSinkJson()
    {
        JsonObject root = new();
        foreach (
            SettingEntry entry in SettingsSchema.All.Where(candidate =>
                candidate.Scope == "user, project"
            )
        )
        {
            SetPath(root, entry.Name, ProbeIds(entry.Name), Probes[entry.Name]);
        }

        root["profiles"]!["probe-two"] = new JsonObject
        {
            ["provider"] = ProbeProvider,
            ["model"] = ProbeModel,
        };
        return root.ToJsonString() + "\n";
    }

    /// <summary>
    /// Builds the project settings file carrying exactly one probe, the shape the user-only
    /// refusal test verifies against the scope scan, the provider scan or the merge. The
    /// capabilities refusal happens in the merge, so its probe carries the profile body that
    /// gets the merge that far.
    /// </summary>
    private static string ScopedProbeJson(string name)
    {
        JsonObject root = new() { ["schemaVersion"] = 2 };
        SetPath(root, name, ProbeIds(name), Probes[name]);
        if (name.StartsWith("profiles.*.capabilities", StringComparison.Ordinal))
        {
            JsonObject profile = (JsonObject)root["profiles"]![WildcardId]!;
            profile["provider"] = ProbeProvider;
            profile["model"] = ProbeModel;
        }

        return root.ToJsonString() + "\n";
    }

    /// <summary>
    /// The id one wildcard stands for: profile and provider probes share one name so the
    /// decision and usage fixtures can reference them; the key remap needs a real binding id.
    /// </summary>
    private static string ProbeIds(string name) =>
        name.StartsWith("tui.keys", StringComparison.Ordinal)
            ? KeyBindingCatalog.Snapshot().First().Id
            : WildcardId;

    /// <summary>
    /// Places one probe into the JSON tree. A wildcard segment stands for the id as one whole
    /// path segment — never a string replace — because a binding id itself contains dots.
    /// </summary>
    private static void SetPath(JsonObject root, string name, string id, object? value)
    {
        string[] parts = name.Split('.');
        JsonObject node = root;
        foreach (string part in parts[..^1])
        {
            string child = Wildcard(part) ? id : part;
            if (node[child] is not JsonObject grand)
            {
                grand = [];
                node[child] = grand;
            }

            node = grand;
        }

        node[Wildcard(parts[^1]) ? id : parts[^1]] = ToNode(value);
    }

    private static bool Wildcard(string segment) =>
        string.Equals(segment, "*", StringComparison.Ordinal);

    private static JsonNode ToNode(object? value)
    {
        switch (value)
        {
            case int number:
                return JsonValue.Create(number);
            case double number:
                return JsonValue.Create(number);
            case bool flag:
                return JsonValue.Create(flag);
            case string text:
                return JsonValue.Create(text);
            case IEnumerable<string> probes:
                JsonArray array = [];
                foreach (string item in probes)
                {
                    array.Add(item);
                }

                return array;
            default:
                throw new InvalidOperationException(
                    $"no probe node builds for a probe value of {value?.GetType().Name}"
                );
        }
    }

    private static SettingEntry Entry(string name) =>
        SettingsSchema.All.First(entry =>
            string.Equals(entry.Name, name, StringComparison.Ordinal)
        );

    /// <summary>
    /// The canonical spelling of one declared key, used as the <c>tui.keys</c> probe: whatever
    /// the remap writer persists is what the reader must accept.
    /// </summary>
    private static string ProbeKeySpec()
    {
        (string Id, KeyPattern[] Keys) binding = KeyBindingCatalog.Snapshot().First();
        return KeyPatternFormatter.Describe(binding.Keys[0]);
    }

    private static ProviderEntry SettingsProvider(string name)
    {
        bool valid = ProviderEntry.TryCreate(
            name,
            "openai",
            new Uri("https://work.example.test/v1"),
            "auth:openai",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }

    /// <summary>
    /// The property-name literals the store switches over, read from the partials' sources. The
    /// store is deny-by-default at every level, so its accepted key set is exactly the set of
    /// literals it compares property names against; a key that resolved by itself would fail the
    /// parser's own unknown-key refusal, so scanning the comparisons is the enumeration.
    /// Captured shapes: <c>switch (property.Name)</c> regions, <c>property.Name ==</c>/
    /// <c>!=</c> comparisons, and single-line <c>is not (x or y)</c> allow-lists.
    /// </summary>
    private static IReadOnlySet<string> AcceptedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            string file in Directory.GetFiles(
                Path.Combine(TestPaths.RepositoryRoot, "src", "Lunate.Coding"),
                "SettingsStore*.cs"
            )
        )
        {
            Collect(File.ReadAllLines(file), keys);
        }

        Assert.True(keys.Count > 20, $"scanned only {keys.Count} accepted keys; the scan broke");
        return keys;
    }

    private static void Collect(string[] lines, ISet<string> keys)
    {
        bool capturing = false;
        bool opened = false;
        int depth = 0;
        foreach (string line in lines)
        {
            if (!capturing && NameSwitch().IsMatch(line))
            {
                capturing = true;
                opened = false;
                depth = 0;
                continue;
            }

            if (capturing)
            {
                // The switch's opening brace lands on the line after its selector; capture case
                // labels until the region's braces close, whatever nestings sit inside.
                depth += line.Count(character => character == '{');
                depth -= line.Count(character => character == '}');
                opened |= line.Contains('{', StringComparison.Ordinal);
                keys.UnionWith(CaseLabel().Matches(line).Select(match => match.Groups[1].Value));
                if (opened && depth <= 0)
                {
                    capturing = false;
                    opened = false;
                }

                continue;
            }

            keys.UnionWith(NameCompare().Matches(line).Select(match => match.Groups[1].Value));
            if (NameIsList().IsMatch(line))
            {
                keys.UnionWith(
                    QuotedLiteral().Matches(line).Select(match => match.Groups[1].Value)
                );
            }
        }
    }

    private static IReadOnlySet<string> SchemaSegments()
    {
        var segments = new HashSet<string>(StringComparer.Ordinal);
        foreach (SettingEntry entry in SettingsSchema.All)
        {
            segments.UnionWith(
                entry
                    .Name.Split('.')
                    .Where(part => !string.Equals(part, "*", StringComparison.Ordinal))
            );
        }

        return segments;
    }

    [GeneratedRegex(@"^\s*switch \(\w+\.Name\)")]
    private static partial Regex NameSwitch();

    [GeneratedRegex(@"^\s*case ""([^""]+)""")]
    private static partial Regex CaseLabel();

    [GeneratedRegex(@"\b(?:property|field)\.Name (?:==|!=) ""([^""]+)""")]
    private static partial Regex NameCompare();

    [GeneratedRegex(@"\b(?:property|field)\.Name is ")]
    private static partial Regex NameIsList();

    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex QuotedLiteral();
}
