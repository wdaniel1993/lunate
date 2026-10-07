using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// Serializes and parses one session line. The byte shape is frozen by the golden tests in
/// tests/fixtures/sessions: envelope field order, the UTC "O" timestamps, the embedded
/// <see cref="ChatMessage"/> node produced by <see cref="AIJsonUtilities.DefaultOptions"/> and
/// extension payloads written back verbatim.
/// </summary>
internal static partial class SessionFormat
{
    internal const int SchemaVersion = 2;

    private const string HeaderType = "header";
    private const string MessageType = "message";
    private const string CompactionType = "compaction";
    private const string ModelChangeType = "modelChange";
    private const string ActiveToolsType = "activeTools";
    private const string PromptSectionType = "promptSection";
    private const string ChildSessionType = "childSession";
    private const string NestedCallsType = "nestedCalls";
    private const string ExtensionPrefix = "ext/";

    /// <summary>The informational version of the Microsoft.Extensions.AI assembly, recorded in the header.</summary>
    internal static string MeaiVersion { get; } =
        typeof(IChatClient)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "unknown";

    /// <summary>Whether a type is a valid extension type: <c>ext/&lt;extension-id&gt;/&lt;type&gt;</c>.</summary>
    internal static bool IsExtensionType(string type) =>
        type.StartsWith(ExtensionPrefix, StringComparison.Ordinal) && type.Split('/').Length >= 3;

    internal static SessionEntry Parse(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Session line is not valid JSON: {exception.Message}",
                exception
            );
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Session line is not a JSON object.");
            }

            string type = RequiredString(root, "type");
            return type switch
            {
                HeaderType => new SessionHeaderEntry(
                    RequiredString(root, "id"),
                    RequiredInt(root, "schema"),
                    RequiredString(root, "cwd"),
                    RequiredTimestamp(root, "created"),
                    RequiredString(root, "meai"),
                    OptionalString(root, "repo"),
                    OptionalString(root, "worktree")
                ),
                MessageType => ParseMessage(root),
                CompactionType => new SessionCompactionEntry(
                    RequiredString(root, "id"),
                    OptionalString(root, "parentId"),
                    RequiredTimestamp(root, "timestamp"),
                    RequiredString(root, "summary"),
                    RequiredStrings(root, "replaces")
                ),
                ModelChangeType => new SessionModelChangeEntry(
                    RequiredString(root, "id"),
                    OptionalString(root, "parentId"),
                    RequiredTimestamp(root, "timestamp"),
                    RequiredString(root, "model")
                ),
                ActiveToolsType => new SessionActiveToolsEntry(
                    RequiredString(root, "id"),
                    OptionalString(root, "parentId"),
                    RequiredTimestamp(root, "timestamp"),
                    RequiredStrings(root, "tools")
                ),
                PromptSectionType => new SessionPromptSectionEntry(
                    RequiredString(root, "id"),
                    OptionalString(root, "parentId"),
                    RequiredTimestamp(root, "timestamp"),
                    RequiredString(root, "section"),
                    RequiredString(root, "text")
                ),
                ChildSessionType => new SessionChildSessionEntry(
                    RequiredString(root, "id"),
                    OptionalString(root, "parentId"),
                    RequiredTimestamp(root, "timestamp"),
                    RequiredString(root, "childSessionId"),
                    RequiredString(root, "runId")
                ),
                NestedCallsType => ParseNestedCalls(root),
                _ => type.StartsWith(ExtensionPrefix, StringComparison.Ordinal)
                    ? ParseExtension(root, type)
                    : throw new InvalidDataException(
                        $"Session line has unknown type '{type}'; expected '{HeaderType}', '{MessageType}', '{CompactionType}', '{ModelChangeType}', '{ActiveToolsType}', '{PromptSectionType}', '{ChildSessionType}', '{NestedCallsType}' or 'ext/<extension-id>/<type>'."
                    ),
            };
        }
    }

    private static SessionMessageEntry ParseMessage(JsonElement root)
    {
        if (
            !root.TryGetProperty("message", out JsonElement messageElement)
            || messageElement.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException(
                "Session message line is missing the embedded 'message' object."
            );
        }

        ChatMessage message =
            messageElement.Deserialize<ChatMessage>(AIJsonUtilities.DefaultOptions)
            ?? throw new InvalidDataException("Session message line has a null 'message'.");

        string rawMessageJson = messageElement.GetRawText();
        SessionUsage? usage =
            root.TryGetProperty("usage", out JsonElement usageElement)
            && usageElement.ValueKind == JsonValueKind.Object
                ? usageElement.Deserialize<SessionUsage>(AIJsonUtilities.DefaultOptions)
                : null;

        SessionMessageEntry entry = new(
            RequiredString(root, "id"),
            OptionalString(root, "parentId"),
            RequiredTimestamp(root, "timestamp"),
            message,
            OptionalString(root, "model"),
            usage
        );

        return JsonNode.DeepEquals(
            JsonNode.Parse(rawMessageJson),
            JsonSerializer.SerializeToNode(message, AIJsonUtilities.DefaultOptions)
        )
            ? entry
            : entry with
            {
                RawMessageJson = rawMessageJson,
            };
    }

    private static SessionNestedCallsEntry ParseNestedCalls(JsonElement root)
    {
        if (
            !root.TryGetProperty("calls", out JsonElement callsElement)
            || callsElement.ValueKind != JsonValueKind.Array
        )
        {
            throw new InvalidDataException(
                "Session nestedCalls line is missing the 'calls' array."
            );
        }

        List<SessionNestedCall> calls = [];
        foreach (JsonElement call in callsElement.EnumerateArray())
        {
            calls.Add(
                new SessionNestedCall(
                    RequiredString(call, "name"),
                    RequiredString(call, "args"),
                    RequiredString(call, "status"),
                    RequiredInt(call, "durationMs")
                )
            );
        }

        return new SessionNestedCallsEntry(
            RequiredString(root, "id"),
            OptionalString(root, "parentId"),
            RequiredTimestamp(root, "timestamp"),
            RequiredString(root, "callId"),
            calls
        );
    }

    private static SessionExtensionEntry ParseExtension(JsonElement root, string type)
    {
        if (!IsExtensionType(type))
        {
            throw new InvalidDataException(
                $"Session line type '{type}' is not a valid extension type; expected 'ext/<extension-id>/<type>'."
            );
        }

        if (!root.TryGetProperty("payload", out JsonElement payload))
        {
            throw new InvalidDataException(
                "Session extension line is missing the 'payload' field."
            );
        }

        return new SessionExtensionEntry(
            RequiredString(root, "id"),
            OptionalString(root, "parentId"),
            RequiredTimestamp(root, "timestamp"),
            type,
            payload.GetRawText()
        );
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (
            !root.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.String
        )
        {
            throw new InvalidDataException(
                $"Session line property '{name}' is missing or not a string."
            );
        }

        return element.GetString()!;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw new InvalidDataException($"Session line property '{name}' is not a string."),
        };
    }

    private static int RequiredInt(JsonElement root, string name)
    {
        if (
            !root.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out int value)
        )
        {
            throw new InvalidDataException(
                $"Session line property '{name}' is missing or not an integer."
            );
        }

        return value;
    }

    private static DateTimeOffset RequiredTimestamp(JsonElement root, string name)
    {
        string text = RequiredString(root, name);
        if (
            !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset value
            )
        )
        {
            throw new InvalidDataException(
                $"Session line property '{name}' is not a valid timestamp: '{text}'."
            );
        }

        return value;
    }

    private static IReadOnlyList<string> RequiredStrings(JsonElement root, string name)
    {
        if (
            !root.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
        )
        {
            throw new InvalidDataException(
                $"Session line property '{name}' is missing or not an array."
            );
        }

        List<string> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    $"Session line property '{name}' must contain strings."
                );
            }

            values.Add(item.GetString()!);
        }

        return values;
    }
}
