using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The write path of <see cref="SessionFormat"/>: schema 1 and 2 lines on a single
/// <see cref="Utf8JsonWriter"/> so raw-preserved parts embed with <c>WriteRawValue</c>.
/// </summary>
internal static partial class SessionFormat
{
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
}
