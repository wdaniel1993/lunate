using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// Serializes and parses one session line. The byte shape is frozen by the golden tests in
/// tests/fixtures/sessions: envelope field order, the UTC "O" timestamps and the embedded
/// <see cref="ChatMessage"/> node produced by <see cref="AIJsonUtilities.DefaultOptions"/>.
/// </summary>
internal static class SessionFormat
{
    internal const int SchemaVersion = 1;

    private const string HeaderType = "header";
    private const string MessageType = "message";
    private const string CompactionType = "compaction";
    private const string ModelChangeType = "modelChange";

    private static readonly JsonSerializerOptions LineJson = new(AIJsonUtilities.DefaultOptions)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>The informational version of the Microsoft.Extensions.AI assembly, recorded in the header.</summary>
    internal static string MeaiVersion { get; } =
        typeof(IChatClient)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "unknown";

    internal static string Serialize(SessionEntry entry) =>
        entry switch
        {
            SessionHeaderEntry header => SerializeLine(
                new HeaderLine(
                    HeaderType,
                    header.Schema,
                    header.Id,
                    header.Cwd,
                    Stamp(header.Created),
                    header.Meai
                )
            ),
            SessionMessageEntry message => SerializeLine(
                new MessageLine(
                    MessageType,
                    message.Id,
                    message.ParentId,
                    Stamp(message.Timestamp),
                    JsonSerializer.SerializeToNode(message.Message, AIJsonUtilities.DefaultOptions),
                    message.Model,
                    message.Usage
                )
            ),
            SessionCompactionEntry compaction => SerializeLine(
                new CompactionLine(
                    CompactionType,
                    compaction.Id,
                    compaction.ParentId,
                    Stamp(compaction.Timestamp),
                    compaction.Summary,
                    [.. compaction.Replaces]
                )
            ),
            SessionModelChangeEntry modelChange => SerializeLine(
                new ModelChangeLine(
                    ModelChangeType,
                    modelChange.Id,
                    modelChange.ParentId,
                    Stamp(modelChange.Timestamp),
                    modelChange.Model
                )
            ),
            _ => throw new InvalidDataException(
                $"Session entry type '{entry.GetType().Name}' cannot be serialized."
            ),
        };

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
                    RequiredString(root, "meai")
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
                _ => throw new InvalidDataException(
                    $"Session line has unknown type '{type}'; expected '{HeaderType}', '{MessageType}', '{CompactionType}' or '{ModelChangeType}'."
                ),
            };
        }
    }

    private static string SerializeLine<TLine>(TLine line) =>
        JsonSerializer.Serialize(line, LineJson);

    private static string Stamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

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

        SessionUsage? usage =
            root.TryGetProperty("usage", out JsonElement usageElement)
            && usageElement.ValueKind == JsonValueKind.Object
                ? usageElement.Deserialize<SessionUsage>(LineJson)
                : null;

        return new SessionMessageEntry(
            RequiredString(root, "id"),
            OptionalString(root, "parentId"),
            RequiredTimestamp(root, "timestamp"),
            message,
            OptionalString(root, "model"),
            usage
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

    private sealed record HeaderLine(
        string Type,
        int Schema,
        string Id,
        string Cwd,
        string Created,
        string Meai
    );

    private sealed record MessageLine(
        string Type,
        string Id,
        string? ParentId,
        string Timestamp,
        JsonNode? Message,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Model,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionUsage? Usage
    );

    private sealed record CompactionLine(
        string Type,
        string Id,
        string? ParentId,
        string Timestamp,
        string Summary,
        IReadOnlyList<string> Replaces
    );

    private sealed record ModelChangeLine(
        string Type,
        string Id,
        string? ParentId,
        string Timestamp,
        string Model
    );
}
