using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
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
internal static class SessionFormat
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

    internal static string Serialize(SessionEntry entry)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (
            var writer = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
            )
        )
        {
            WriteEntry(writer, entry);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

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

    private static void WriteEntry(Utf8JsonWriter writer, SessionEntry entry)
    {
        switch (entry)
        {
            case SessionHeaderEntry header:
                WriteHeader(writer, header);
                break;
            case SessionMessageEntry message:
                WriteMessage(writer, message);
                break;
            case SessionCompactionEntry compaction:
                WriteCompaction(writer, compaction);
                break;
            case SessionModelChangeEntry modelChange:
                WriteModelChange(writer, modelChange);
                break;
            case SessionActiveToolsEntry activeTools:
                WriteActiveTools(writer, activeTools);
                break;
            case SessionPromptSectionEntry promptSection:
                WritePromptSection(writer, promptSection);
                break;
            case SessionChildSessionEntry childSession:
                WriteChildSession(writer, childSession);
                break;
            case SessionNestedCallsEntry nestedCalls:
                WriteNestedCalls(writer, nestedCalls);
                break;
            case SessionExtensionEntry extension:
                WriteExtension(writer, extension);
                break;
            default:
                throw new InvalidDataException(
                    $"Session entry type '{entry.GetType().Name}' cannot be serialized."
                );
        }
    }

    private static void WriteHeader(Utf8JsonWriter writer, SessionHeaderEntry header)
    {
        writer.WriteStartObject();
        writer.WriteString("type", HeaderType);
        writer.WriteNumber("schema", header.Schema);
        writer.WriteString("id", header.Id);
        writer.WriteString("cwd", header.Cwd);
        writer.WriteString("created", Stamp(header.Created));
        writer.WriteString("meai", header.Meai);
        if (header.Repo is { } repo)
        {
            writer.WriteString("repo", repo);
        }

        if (header.Worktree is { } worktree)
        {
            writer.WriteString("worktree", worktree);
        }

        writer.WriteEndObject();
    }

    private static void WriteMessage(Utf8JsonWriter writer, SessionMessageEntry message)
    {
        writer.WriteStartObject();
        WriteEnvelope(writer, MessageType, message.Id, message.ParentId, message.Timestamp);
        writer.WritePropertyName("message");
        if (message.RawMessageJson is { } raw)
        {
            writer.WriteRawValue(raw);
        }
        else
        {
            JsonSerializer
                .SerializeToNode(message.Message, AIJsonUtilities.DefaultOptions)!
                .WriteTo(writer, AIJsonUtilities.DefaultOptions);
        }

        if (message.Model is { } model)
        {
            writer.WriteString("model", model);
        }

        if (message.Usage is { } usage)
        {
            writer.WritePropertyName("usage");
            writer.WriteStartObject();
            writer.WriteNumber("input", usage.Input);
            writer.WriteNumber("output", usage.Output);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteCompaction(Utf8JsonWriter writer, SessionCompactionEntry compaction)
    {
        writer.WriteStartObject();
        WriteEnvelope(
            writer,
            CompactionType,
            compaction.Id,
            compaction.ParentId,
            compaction.Timestamp
        );
        writer.WriteString("summary", compaction.Summary);
        WriteStrings(writer, "replaces", compaction.Replaces);
        writer.WriteEndObject();
    }

    private static void WriteModelChange(Utf8JsonWriter writer, SessionModelChangeEntry modelChange)
    {
        writer.WriteStartObject();
        WriteEnvelope(
            writer,
            ModelChangeType,
            modelChange.Id,
            modelChange.ParentId,
            modelChange.Timestamp
        );
        writer.WriteString("model", modelChange.Model);
        writer.WriteEndObject();
    }

    private static void WriteActiveTools(Utf8JsonWriter writer, SessionActiveToolsEntry activeTools)
    {
        writer.WriteStartObject();
        WriteEnvelope(
            writer,
            ActiveToolsType,
            activeTools.Id,
            activeTools.ParentId,
            activeTools.Timestamp
        );
        WriteStrings(writer, "tools", activeTools.Tools);
        writer.WriteEndObject();
    }

    private static void WritePromptSection(
        Utf8JsonWriter writer,
        SessionPromptSectionEntry promptSection
    )
    {
        writer.WriteStartObject();
        WriteEnvelope(
            writer,
            PromptSectionType,
            promptSection.Id,
            promptSection.ParentId,
            promptSection.Timestamp
        );
        writer.WriteString("section", promptSection.Section);
        writer.WriteString("text", promptSection.Text);
        writer.WriteEndObject();
    }

    private static void WriteChildSession(
        Utf8JsonWriter writer,
        SessionChildSessionEntry childSession
    )
    {
        writer.WriteStartObject();
        WriteEnvelope(
            writer,
            ChildSessionType,
            childSession.Id,
            childSession.ParentId,
            childSession.Timestamp
        );
        writer.WriteString("childSessionId", childSession.ChildSessionId);
        writer.WriteString("runId", childSession.RunId);
        writer.WriteEndObject();
    }

    private static void WriteNestedCalls(Utf8JsonWriter writer, SessionNestedCallsEntry nestedCalls)
    {
        writer.WriteStartObject();
        WriteEnvelope(
            writer,
            NestedCallsType,
            nestedCalls.Id,
            nestedCalls.ParentId,
            nestedCalls.Timestamp
        );
        writer.WriteString("callId", nestedCalls.CallId);
        writer.WritePropertyName("calls");
        writer.WriteStartArray();
        foreach (SessionNestedCall call in nestedCalls.Calls)
        {
            writer.WriteStartObject();
            writer.WriteString("name", call.Name);
            writer.WriteString("args", call.Args);
            writer.WriteString("status", call.Status);
            writer.WriteNumber("durationMs", call.DurationMs);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteExtension(Utf8JsonWriter writer, SessionExtensionEntry extension)
    {
        writer.WriteStartObject();
        WriteEnvelope(
            writer,
            extension.ExtensionType,
            extension.Id,
            extension.ParentId,
            extension.Timestamp
        );
        writer.WritePropertyName("payload");
        writer.WriteRawValue(extension.PayloadJson);
        writer.WriteEndObject();
    }

    private static void WriteEnvelope(
        Utf8JsonWriter writer,
        string type,
        string id,
        string? parentId,
        DateTimeOffset timestamp
    )
    {
        writer.WriteString("type", type);
        writer.WriteString("id", id);
        if (parentId is null)
        {
            writer.WriteNull("parentId");
        }
        else
        {
            writer.WriteString("parentId", parentId);
        }

        writer.WriteString("timestamp", Stamp(timestamp));
    }

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<string> values
    )
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

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
