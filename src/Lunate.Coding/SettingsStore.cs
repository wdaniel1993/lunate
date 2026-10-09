using System.Globalization;
using System.Text.Json;
using Lunate.Agent;

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

    /// <summary>The character budget for one tool result before truncation.</summary>
    public int ToolOutputLimit { get; init; } = ToolOutput.DefaultLimit;
}

/// <summary>
/// Reads <c>~/.lunate/settings.json</c> (schema 1). A missing file yields defaults; a present file
/// is validated with every problem collected into one error, and no partial settings are applied.
/// The environment (<c>LUNATE_MODEL</c>, <c>LUNATE_APPROVAL</c>, <c>LUNATE_TOOL_OUTPUT_LIMIT</c>)
/// overrides file values. Both the path and the environment lookup are injectable.
/// </summary>
public static class SettingsStore
{
    /// <summary>The only supported schema version.</summary>
    public const int SupportedSchemaVersion = 1;

    private const string ModelVariable = "LUNATE_MODEL";
    private const string ApprovalVariable = "LUNATE_APPROVAL";
    private const string ToolOutputLimitVariable = "LUNATE_TOOL_OUTPUT_LIMIT";

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

    private static AgentSettings ParseFile(string path, List<string> problems)
    {
        var settings = new AgentSettings();
        if (!File.Exists(path))
        {
            return settings;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
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
                            || version != SupportedSchemaVersion
                        )
                        {
                            problems.Add($"schemaVersion must be {SupportedSchemaVersion}");
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
                    case "output":
                        settings = settings with
                        {
                            ToolOutputLimit = ReadOutput(property.Value, problems),
                        };
                        break;
                    default:
                        problems.Add($"unknown key '{property.Name}'");
                        break;
                }
            }

            if (!schemaVersionSeen)
            {
                problems.Add($"schemaVersion is missing; expected {SupportedSchemaVersion}");
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

        return settings;
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
