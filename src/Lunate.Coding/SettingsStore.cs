using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>How tool calls are handled once the non-interactive approver consumes the setting.</summary>
public enum ApprovalPolicy
{
    /// <summary>Read-only tools run; file writes, edits and commands require approval (default).</summary>
    Ask,

    /// <summary>File writes and edits run; commands require approval.</summary>
    AutoEdit,
}

/// <summary>The resolved agent settings: file values overridden by the environment.</summary>
public sealed record AgentSettings
{
    /// <summary>The default model id, or null when unset.</summary>
    public string? Model { get; init; }

    /// <summary>The approval policy; defaults to <see cref="ApprovalPolicy.Ask"/>.</summary>
    public ApprovalPolicy Approval { get; init; } = ApprovalPolicy.Ask;

    /// <summary>How much of a tool call the scrollback shows; defaults to collapsed.</summary>
    public ToolDetails ToolDetails { get; init; } = ToolDetails.Collapsed;

    /// <summary>The character budget for one tool result before truncation.</summary>
    public int ToolOutputLimit { get; init; } = ToolOutput.DefaultLimit;

    /// <summary>The resolved subagent limits (the <c>subagents</c> section; defaults 4/100000/500000).</summary>
    public SubagentLimits Subagents { get; init; } = new();

    /// <summary>The resolved Roslyn settings (the <c>roslyn</c> section).</summary>
    public RoslynSettings Roslyn { get; init; } = new();

    /// <summary>Whether the interactive session prints the welcome header; defaults to on.</summary>
    public bool StartupHeader { get; init; } = true;

    /// <summary>The session thinking level; defaults to <see cref="ThinkingLevel.Off"/>.</summary>
    public ThinkingLevel Thinking { get; init; } = ThinkingLevel.Off;

    /// <summary>Whether the committed reasoning prints in full instead of collapsed; defaults to off.</summary>
    public bool ThinkingFull { get; init; }

    /// <summary>The ordered favourite model ids (at most eight, unique); schema 2 and later.</summary>
    public IReadOnlyList<string> Favorites { get; init; } = [];
}

/// <summary>The per-workspace Roslyn limits (the <c>roslyn</c> settings section).</summary>
public sealed record RoslynSettings
{
    /// <summary>The documented default of <see cref="MaxLoadedWorkspaces"/>.</summary>
    public const int DefaultMaxLoadedWorkspaces = 2;

    /// <summary>
    /// The maximum number of loaded Roslyn workspaces across roots; a further root's load evicts
    /// the least-recently-used unpinned backend. Never <c>unlimited</c>: the doubled worst-case
    /// memory (up to 2× the ADR-0009 Roslyn budget) is the documented cost of concurrency.
    /// </summary>
    public int MaxLoadedWorkspaces { get; init; } = DefaultMaxLoadedWorkspaces;
}

/// <summary>
/// Reads <c>~/.lunate/settings.json</c> (schema 2; schema 1 reads as the same settings without
/// favourites). A missing file yields defaults; a present file is validated with every problem
/// collected into one error, and no partial settings are applied. The environment
/// (<c>LUNATE_MODEL</c>, <c>LUNATE_APPROVAL</c>, <c>LUNATE_TOOL_DETAILS</c>,
/// <c>LUNATE_TOOL_OUTPUT_LIMIT</c>, <c>LUNATE_STARTUP_HEADER</c>,
/// <c>LUNATE_SUBAGENTS_CONCURRENCY</c>, <c>LUNATE_SUBAGENTS_TOKEN_BUDGET</c>,
/// <c>LUNATE_SUBAGENTS_TREE_TOKEN_BUDGET</c>) overrides file values. Both the path and the
/// environment lookup are injectable.
/// </summary>
public static class SettingsStore
{
    /// <summary>The schema this build writes.</summary>
    public const int SupportedSchemaVersion = 2;

    /// <summary>The oldest schema this build still reads.</summary>
    public const int OldestSupportedSchemaVersion = 1;

    private const string SupportedSchemaVersions = "1 or 2";

    private const string ModelVariable = "LUNATE_MODEL";
    private const string ApprovalVariable = "LUNATE_APPROVAL";
    private const string ToolDetailsVariable = "LUNATE_TOOL_DETAILS";
    private const string ToolOutputLimitVariable = "LUNATE_TOOL_OUTPUT_LIMIT";
    private const string StartupHeaderVariable = "LUNATE_STARTUP_HEADER";
    private const string SubagentConcurrencyVariable = "LUNATE_SUBAGENTS_CONCURRENCY";
    private const string SubagentTokenBudgetVariable = "LUNATE_SUBAGENTS_TOKEN_BUDGET";
    private const string SubagentTreeTokenBudgetVariable = "LUNATE_SUBAGENTS_TREE_TOKEN_BUDGET";

    /// <summary>The one explicit literal that disables a subagent limit.</summary>
    private const string Unlimited = "unlimited";

    /// <summary>The literals a boolean switch setting accepts, as the error message lists them.</summary>
    private const string SwitchLiterals = "true, false, 1 or 0";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Resolves the effective settings; throws one <see cref="InvalidDataException"/> naming every problem.</summary>
    public static AgentSettings Resolve(
        string? filePath = null,
        Func<string, string?>? environment = null
    )
    {
        string path = filePath ?? DefaultFilePath();
        var problems = new List<string>();
        AgentSettings settings = ParseFile(path, problems);
        settings = ApplyEnvironment(
            settings,
            environment ?? Environment.GetEnvironmentVariable,
            problems
        );
        if (problems.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid settings in '{path}': {string.Join("; ", problems)}. No settings were applied."
            );
        }

        return settings;
    }

    /// <summary>
    /// Writes the favourites through read-modify-write: every other value in the existing JSON is
    /// preserved, the result is validated by the same parser, and the file is replaced atomically
    /// with <c>schemaVersion: 2</c>. On any failure the old file is left untouched.
    /// </summary>
    public static void WriteFavorites(IReadOnlyList<string> favorites, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        string path = filePath ?? DefaultFilePath();
        JsonObject root = ReadRoot(path);
        root["schemaVersion"] = SupportedSchemaVersion;
        var list = new JsonArray();
        foreach (string id in favorites)
        {
            list.Add(id);
        }

        root["favorites"] = list;

        string json = root.ToJsonString(WriteOptions) + "\n";
        var problems = new List<string>();
        _ = ParseJson(json, problems);
        if (problems.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid settings in '{path}': {string.Join("; ", problems)}. No settings were written."
            );
        }

        WriteAtomically(path, json);
    }

    private static JsonObject ReadRoot(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid settings in '{path}': the file is not valid JSON ({exception.Message}). No settings were written.",
                exception
            );
        }

        return node as JsonObject
            ?? throw new InvalidDataException(
                $"Invalid settings in '{path}': the root must be a JSON object. No settings were written."
            );
    }

    private static void WriteAtomically(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string targetDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
        string temp = Path.Combine(targetDirectory, Path.GetRandomFileName() + ".tmp");
        try
        {
            File.WriteAllText(temp, content);
            if (!OperatingSystem.IsWindows() && File.Exists(path))
            {
                File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static AgentSettings ParseFile(string path, List<string> problems)
    {
        if (!File.Exists(path))
        {
            return new AgentSettings();
        }

        return ParseJson(File.ReadAllText(path), problems);
    }

    private static AgentSettings ParseJson(string json, List<string> problems)
    {
        var settings = new AgentSettings();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            problems.Add($"the file is not valid JSON ({exception.Message})");
            return settings;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add("the root must be a JSON object");
                return settings;
            }

            bool schemaVersionSeen = false;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "schemaVersion":
                        schemaVersionSeen = true;
                        if (
                            property.Value.ValueKind != JsonValueKind.Number
                            || !property.Value.TryGetInt32(out int version)
                            || version < OldestSupportedSchemaVersion
                            || version > SupportedSchemaVersion
                        )
                        {
                            problems.Add($"schemaVersion must be {SupportedSchemaVersions}");
                        }

                        break;
                    case "model":
                        settings = settings with { Model = ReadModel(property.Value, problems) };
                        break;
                    case "approval":
                        settings = settings with
                        {
                            Approval = ReadApproval(property.Value, "approval", problems),
                        };
                        break;
                    case "toolDetails":
                        settings = settings with
                        {
                            ToolDetails = ReadToolDetails(
                                property.Value,
                                "'toolDetails'",
                                problems
                            ),
                        };
                        break;
                    case "output":
                        settings = settings with
                        {
                            ToolOutputLimit = ReadOutput(property.Value, problems),
                        };
                        break;
                    case "subagents":
                        settings = settings with
                        {
                            Subagents = ReadSubagents(property.Value, problems),
                        };
                        break;
                    case "roslyn":
                        settings = settings with { Roslyn = ReadRoslyn(property.Value, problems) };
                        break;
                    case "startupHeader":
                        settings = settings with
                        {
                            StartupHeader = ReadStartupHeader(property.Value, problems),
                        };
                        break;
                    case "thinking":
                        settings = settings with
                        {
                            Thinking = ReadThinking(property.Value, problems),
                        };
                        break;
                    case "thinkingFull":
                        settings = settings with
                        {
                            ThinkingFull = ReadThinkingFull(property.Value, problems),
                        };
                        break;
                    case "favorites":
                        settings = settings with
                        {
                            Favorites = ReadFavorites(property.Value, problems),
                        };
                        break;
                    default:
                        problems.Add($"unknown key '{property.Name}'");
                        break;
                }
            }

            if (!schemaVersionSeen)
            {
                problems.Add($"schemaVersion is missing; expected {SupportedSchemaVersions}");
            }
        }

        return settings;
    }

    private static string? ReadModel(JsonElement value, List<string> problems)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            problems.Add("'model' must be a non-empty string");
            return null;
        }

        return value.GetString();
    }

    private static ApprovalPolicy ReadApproval(JsonElement value, string key, List<string> problems)
    {
        if (
            value.ValueKind == JsonValueKind.String
            && TryParseApproval(value.GetString(), out var parsed)
        )
        {
            return parsed;
        }

        problems.Add(
            ApprovalError(
                $"'{key}'",
                value.ValueKind == JsonValueKind.String ? value.GetString() : null
            )
        );
        return ApprovalPolicy.Ask;
    }

    /// <summary>
    /// Reads <c>toolDetails</c>: the exact lowercase literal; a missing key keeps the documented
    /// default (collapsed).
    /// </summary>
    private static ToolDetails ReadToolDetails(
        JsonElement value,
        string subject,
        List<string> problems
    )
    {
        if (
            value.ValueKind == JsonValueKind.String
            && TryParseToolDetails(value.GetString(), out var parsed)
        )
        {
            return parsed;
        }

        problems.Add(
            ToolDetailsError(
                subject,
                value.ValueKind == JsonValueKind.String ? value.GetString() : null
            )
        );
        return ToolDetails.Collapsed;
    }

    private static int ReadOutput(JsonElement value, List<string> problems)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'output' must be an object");
            return ToolOutput.DefaultLimit;
        }

        int limit = ToolOutput.DefaultLimit;
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Name != "toolResultLimit")
            {
                problems.Add($"unknown key 'output.{property.Name}'");
                continue;
            }

            if (
                property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetInt32(out int parsed)
                || parsed < 1
            )
            {
                problems.Add("'output.toolResultLimit' must be a positive integer");
                continue;
            }

            limit = parsed;
        }

        return limit;
    }

    /// <summary>
    /// Reads the <c>subagents</c> section: each key is a positive integer or the explicit literal
    /// <c>unlimited</c>; a missing key keeps the documented default.
    /// </summary>
    private static SubagentLimits ReadSubagents(JsonElement value, List<string> problems)
    {
        var limits = new SubagentLimits();
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'subagents' must be an object");
            return limits;
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "concurrency":
                    limits = limits with
                    {
                        Concurrency = ReadConcurrency(property.Value, problems),
                    };
                    break;
                case "tokenBudget":
                    limits = limits with
                    {
                        TokenBudget = ReadTokenBudget(
                            property.Value,
                            "subagents.tokenBudget",
                            fallback: 100_000,
                            problems
                        ),
                    };
                    break;
                case "treeTokenBudget":
                    limits = limits with
                    {
                        TreeTokenBudget = ReadTokenBudget(
                            property.Value,
                            "subagents.treeTokenBudget",
                            fallback: 500_000,
                            problems
                        ),
                    };
                    break;
                default:
                    problems.Add($"unknown key 'subagents.{property.Name}'");
                    break;
            }
        }

        return limits;
    }

    /// <summary>
    /// Reads the <c>roslyn</c> section: <c>maxLoadedWorkspaces</c> is a positive integer; a
    /// missing key keeps the documented default. <c>unlimited</c> is not offered.
    /// </summary>
    private static RoslynSettings ReadRoslyn(JsonElement value, List<string> problems)
    {
        var settings = new RoslynSettings();
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'roslyn' must be an object");
            return settings;
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Name != "maxLoadedWorkspaces")
            {
                problems.Add($"unknown key 'roslyn.{property.Name}'");
                continue;
            }

            if (
                property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetInt32(out int parsed)
                || parsed < 1
            )
            {
                problems.Add("'roslyn.maxLoadedWorkspaces' must be a positive integer");
                continue;
            }

            settings = settings with { MaxLoadedWorkspaces = parsed };
        }

        return settings;
    }

    /// <summary>
    /// Reads <c>startupHeader</c>: a JSON boolean; a missing key keeps the documented default
    /// (on).
    /// </summary>
    private static bool ReadStartupHeader(JsonElement value, List<string> problems)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        problems.Add("'startupHeader' must be true or false");
        return true;
    }

    /// <summary>
    /// Reads <c>thinking</c>: the level literal (exact lowercase); a missing key keeps the
    /// documented default (off).
    /// </summary>
    private static ThinkingLevel ReadThinking(JsonElement value, List<string> problems)
    {
        if (
            value.ValueKind == JsonValueKind.String
            && TryParseThinking(value.GetString(), out var parsed)
        )
        {
            return parsed;
        }

        problems.Add(
            ThinkingError(value.ValueKind == JsonValueKind.String ? value.GetString() : null)
        );
        return ThinkingLevel.Off;
    }

    /// <summary>
    /// Reads <c>thinkingFull</c>: a JSON boolean; a missing key keeps the documented default
    /// (collapsed).
    /// </summary>
    private static bool ReadThinkingFull(JsonElement value, List<string> problems)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        problems.Add("'thinkingFull' must be true or false");
        return false;
    }

    /// <summary>
    /// Reads <c>favorites</c>: an ordered array of at most eight unique non-empty model ids. Every
    /// problem is collected; a refused list applies nothing.
    /// </summary>
    private static IReadOnlyList<string> ReadFavorites(JsonElement value, List<string> problems)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            problems.Add("'favorites' must be an array of model ids");
            return [];
        }

        var favorites = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
            {
                problems.Add("'favorites' must contain only non-empty model ids");
                continue;
            }

            string id = item.GetString()!;
            if (!seen.Add(id))
            {
                problems.Add($"'favorites' must be unique; '{id}' appears more than once");
                continue;
            }

            favorites.Add(id);
        }

        if (favorites.Count > 8)
        {
            problems.Add("'favorites' is limited to 8 entries");
        }

        return favorites;
    }

    private static int? ReadConcurrency(JsonElement value, List<string> problems)
    {
        if (IsUnlimited(value))
        {
            return null;
        }

        if (
            value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int parsed)
            && parsed >= 1
        )
        {
            return parsed;
        }

        problems.Add("'subagents.concurrency' must be a positive integer or 'unlimited'");
        return new SubagentLimits().Concurrency;
    }

    private static long? ReadTokenBudget(
        JsonElement value,
        string key,
        long fallback,
        List<string> problems
    )
    {
        if (IsUnlimited(value))
        {
            return null;
        }

        if (
            value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long parsed)
            && parsed >= 1
        )
        {
            return parsed;
        }

        problems.Add($"'{key}' must be a positive integer or 'unlimited'");
        return fallback;
    }

    private static bool IsUnlimited(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() == Unlimited;

    private static AgentSettings ApplyEnvironment(
        AgentSettings settings,
        Func<string, string?> environment,
        List<string> problems
    )
    {
        string? model = environment(ModelVariable);
        if (!string.IsNullOrEmpty(model))
        {
            settings = settings with { Model = model };
        }

        string? approval = environment(ApprovalVariable);
        if (!string.IsNullOrEmpty(approval))
        {
            if (TryParseApproval(approval, out var parsed))
            {
                settings = settings with { Approval = parsed };
            }
            else
            {
                problems.Add(ApprovalError(ApprovalVariable, approval));
            }
        }

        string? toolDetails = environment(ToolDetailsVariable);
        if (!string.IsNullOrEmpty(toolDetails))
        {
            if (TryParseToolDetails(toolDetails, out var parsedDetails))
            {
                settings = settings with { ToolDetails = parsedDetails };
            }
            else
            {
                problems.Add(ToolDetailsError(ToolDetailsVariable, toolDetails));
            }
        }

        string? limit = environment(ToolOutputLimitVariable);
        if (!string.IsNullOrEmpty(limit))
        {
            if (
                int.TryParse(
                    limit,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed
                )
                && parsed >= 1
            )
            {
                settings = settings with { ToolOutputLimit = parsed };
            }
            else
            {
                problems.Add(
                    $"{ToolOutputLimitVariable} must be a positive integer, but was '{limit}'"
                );
            }
        }

        string? startupHeader = environment(StartupHeaderVariable);
        if (!string.IsNullOrEmpty(startupHeader))
        {
            if (TryParseSwitch(startupHeader, out bool parsedHeader))
            {
                settings = settings with { StartupHeader = parsedHeader };
            }
            else
            {
                problems.Add(
                    $"{StartupHeaderVariable} must be {SwitchLiterals}, but was '{startupHeader}'"
                );
            }
        }

        string? concurrency = environment(SubagentConcurrencyVariable);
        if (!string.IsNullOrEmpty(concurrency))
        {
            if (concurrency == Unlimited)
            {
                settings = settings with
                {
                    Subagents = settings.Subagents with { Concurrency = null },
                };
            }
            else if (
                int.TryParse(
                    concurrency,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedConcurrency
                )
                && parsedConcurrency >= 1
            )
            {
                settings = settings with
                {
                    Subagents = settings.Subagents with { Concurrency = parsedConcurrency },
                };
            }
            else
            {
                problems.Add(
                    $"{SubagentConcurrencyVariable} must be a positive integer or 'unlimited', but was '{concurrency}'"
                );
            }
        }

        string? tokenBudget = environment(SubagentTokenBudgetVariable);
        if (!string.IsNullOrEmpty(tokenBudget))
        {
            long? parsed = ParseBudget(tokenBudget, SubagentTokenBudgetVariable, problems);
            if (parsed is not null || tokenBudget == Unlimited)
            {
                settings = settings with
                {
                    Subagents = settings.Subagents with { TokenBudget = parsed },
                };
            }
        }

        string? treeBudget = environment(SubagentTreeTokenBudgetVariable);
        if (!string.IsNullOrEmpty(treeBudget))
        {
            long? parsed = ParseBudget(treeBudget, SubagentTreeTokenBudgetVariable, problems);
            if (parsed is not null || treeBudget == Unlimited)
            {
                settings = settings with
                {
                    Subagents = settings.Subagents with { TreeTokenBudget = parsed },
                };
            }
        }

        return settings;
    }

    private static long? ParseBudget(string value, string variable, List<string> problems)
    {
        if (value == Unlimited)
        {
            return null;
        }

        if (
            long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed
            )
            && parsed >= 1
        )
        {
            return parsed;
        }

        problems.Add($"{variable} must be a positive integer or 'unlimited', but was '{value}'");
        return null;
    }

    /// <summary>The switch literals a setting accepts: <c>true</c>/<c>1</c> and <c>false</c>/<c>0</c>.</summary>
    private static bool TryParseSwitch(string value, out bool parsed)
    {
        switch (value.ToLowerInvariant())
        {
            case "true" or "1":
                parsed = true;
                return true;
            case "false" or "0":
                parsed = false;
                return true;
            default:
                parsed = true;
                return false;
        }
    }

    private static bool TryParseToolDetails(string? value, out ToolDetails parsed)
    {
        switch (value)
        {
            case "collapsed":
                parsed = ToolDetails.Collapsed;
                return true;
            case "expanded":
                parsed = ToolDetails.Expanded;
                return true;
            default:
                parsed = ToolDetails.Collapsed;
                return false;
        }
    }

    private static bool TryParseApproval(string? value, out ApprovalPolicy parsed)
    {
        switch (value)
        {
            case "ask":
                parsed = ApprovalPolicy.Ask;
                return true;
            case "auto-edit":
                parsed = ApprovalPolicy.AutoEdit;
                return true;
            default:
                parsed = ApprovalPolicy.Ask;
                return false;
        }
    }

    /// <summary>
    /// Parses the exact lowercase level literal (shared with the <c>/thinking</c> command, so the
    /// settings vocabulary stays one table).
    /// </summary>
    internal static bool TryParseThinking(string? value, out ThinkingLevel parsed)
    {
        switch (value)
        {
            case "off":
                parsed = ThinkingLevel.Off;
                return true;
            case "low":
                parsed = ThinkingLevel.Low;
                return true;
            case "medium":
                parsed = ThinkingLevel.Medium;
                return true;
            case "high":
                parsed = ThinkingLevel.High;
                return true;
            default:
                parsed = ThinkingLevel.Off;
                return false;
        }
    }

    private static string ThinkingError(string? value) =>
        value is null
            ? "'thinking' must be 'off', 'low', 'medium' or 'high'"
            : $"'thinking' must be 'off', 'low', 'medium' or 'high', but was '{value}'";

    private static string ToolDetailsError(string subject, string? value) =>
        value is null
            ? $"{subject} must be 'collapsed' or 'expanded'"
            : $"{subject} must be 'collapsed' or 'expanded', but was '{value}'";

    private static string ApprovalError(string subject, string? value) =>
        value == "yolo"
            ? $"{subject} is 'yolo'; yolo is a per-run flag, never a saved setting. Use 'ask' or 'auto-edit'"
        : value is null ? $"{subject} must be 'ask' or 'auto-edit'"
        : $"{subject} must be 'ask' or 'auto-edit', but was '{value}'";

    private static string DefaultFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "settings.json"
        );
}
