using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

internal static class FixtureFormat
{
    internal const int SchemaVersion = 1;

    private const string HeaderType = "header";
    private const string ExchangeType = "exchange";
    private const string ContentCacheControlKey = "anthropic:cache_control";
    private const string ImageDigestDiscriminator = "imageDigest";

    internal static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions)
        {
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        AIJsonUtilities.AddAIContentType<ImageDigestContent>(options, ImageDigestDiscriminator);
        return options;
    }

    internal static string SerializeHeader(string modelId, DateTimeOffset recordedAt) =>
        JsonSerializer.Serialize(
            new HeaderLine(
                HeaderType,
                SchemaVersion,
                modelId,
                recordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            ),
            JsonOptions
        );

    internal static string SerializeExchange(
        string requestDigest,
        IReadOnlyList<ChatResponseUpdate> updates
    ) =>
        JsonSerializer.Serialize(
            new ExchangeLine(ExchangeType, requestDigest, [.. updates]),
            JsonOptions
        );

    /// <summary>
    /// Computes the digest that identifies a request: the messages, the model id and the tool names.
    /// Tool descriptions and schemas are deliberately outside the digest, so presentation changes do
    /// not invalidate a fixture (revisit if it bites). Cache breakpoints are request-local metadata,
    /// so they are stripped before hashing: recording and replay stay unaffected by them. Image
    /// bytes are hashed to their media type and SHA-256 digest, so base64 never enters the digest
    /// input and a data content's encoding or name does not change the digest.
    /// </summary>
    internal static string ComputeRequestDigest(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options
    ) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(ComputeDigestInput(messages, options)))
        );

    /// <summary>The JSON input that <see cref="ComputeRequestDigest"/> hashes.</summary>
    internal static string ComputeDigestInput(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options
    )
    {
        var input = new DigestInput(
            DigestMessages(messages),
            options?.ModelId,
            options?.Tools is { } tools ? [.. tools.Select(static tool => tool.Name)] : []
        );
        return JsonSerializer.Serialize(input, JsonOptions);
    }

    private static List<ChatMessage> DigestMessages(IEnumerable<ChatMessage> messages) =>
        [.. messages.Select(DigestMessage)];

    private static ChatMessage DigestMessage(ChatMessage message)
    {
        if (!message.Contents.Any(NeedsDigestCopy))
        {
            return message;
        }

        ChatMessage copy = message.Clone();
        copy.Contents = [.. message.Contents.Select(DigestContent)];
        return copy;
    }

    private static bool NeedsDigestCopy(AIContent content) =>
        HasCacheControl(content) || IsImageData(content);

    private static AIContent DigestContent(AIContent content)
    {
        AIContent result = content;
        if (
            content.AdditionalProperties is { } properties
            && HasCacheControl(content)
            && AnthropicPromptCache.CopyContent(content) is { } copy
        )
        {
            List<KeyValuePair<string, object?>> kept =
            [
                .. properties.Where(entry => entry.Key != ContentCacheControlKey),
            ];
            copy.AdditionalProperties =
                kept.Count == 0 ? null : new AdditionalPropertiesDictionary(kept);
            result = copy;
        }

        if (result is DataContent data && IsImageData(data))
        {
            // Only the pixels and the media type identify the image: DataContent's data URI,
            // base64 encoding, name and other metadata stay out of the digest.
            return new ImageDigestContent(
                data.MediaType,
                Convert.ToHexStringLower(SHA256.HashData(data.Data.Span))
            );
        }

        return result;
    }

    private static bool IsImageData(AIContent content) =>
        content is DataContent data && data.HasTopLevelMediaType("image");

    private static bool HasCacheControl(AIContent content) =>
        content.AdditionalProperties?.ContainsKey(ContentCacheControlKey) == true;

    internal static FixtureDocument ReadFile(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            throw new InvalidDataException(
                $"Fixture '{path}' is empty; expected a '{HeaderType}' line with schema {SchemaVersion}."
            );
        }

        string firstType = ReadType(lines[0], path, 1);
        if (firstType != HeaderType)
        {
            throw new InvalidDataException(
                $"Fixture '{path}' line 1 has type '{firstType}'; expected a '{HeaderType}' line with schema {SchemaVersion}."
            );
        }

        FixtureHeader header = ParseHeader(lines[0], path, 1);
        var exchanges = new List<FixtureExchange>();
        for (int index = 1; index < lines.Length; index++)
        {
            int lineNumber = index + 1;
            string type = ReadType(lines[index], path, lineNumber);
            if (type != ExchangeType)
            {
                throw new InvalidDataException(
                    $"Fixture '{path}' line {lineNumber} has unknown type '{type}'; expected '{ExchangeType}'. Re-record the fixture with LUNATE_RECORD=1."
                );
            }

            exchanges.Add(ParseExchange(lines[index], path, lineNumber));
        }

        return new FixtureDocument(header, exchanges);
    }

    internal static FixtureExchange ParseExchange(string line, string source, int lineNumber)
    {
        ExchangeLine parsed = Deserialize<ExchangeLine>(line, source, lineNumber);
        if (string.IsNullOrEmpty(parsed.RequestDigest))
        {
            throw new InvalidDataException(
                $"Fixture '{source}' line {lineNumber} is missing requestDigest."
            );
        }

        if (parsed.Updates is null)
        {
            throw new InvalidDataException(
                $"Fixture '{source}' line {lineNumber} is missing updates."
            );
        }

        return new FixtureExchange(parsed.RequestDigest, parsed.Updates);
    }

    private static FixtureHeader ParseHeader(string line, string path, int lineNumber)
    {
        HeaderLine parsed = Deserialize<HeaderLine>(line, path, lineNumber);
        if (parsed.Schema != SchemaVersion)
        {
            string schema = parsed.Schema is int value
                ? value.ToString(CultureInfo.InvariantCulture)
                : "missing";
            throw new InvalidDataException(
                $"Fixture '{path}' declares schema '{schema}' but this build supports schema {SchemaVersion}. Re-record the fixture with LUNATE_RECORD=1."
            );
        }

        if (string.IsNullOrEmpty(parsed.Model))
        {
            throw new InvalidDataException($"Fixture '{path}' header is missing model.");
        }

        if (string.IsNullOrEmpty(parsed.RecordedAt))
        {
            throw new InvalidDataException($"Fixture '{path}' header is missing recordedAt.");
        }

        return new FixtureHeader(parsed.Schema.Value, parsed.Model, parsed.RecordedAt);
    }

    private static string ReadType(string line, string path, int lineNumber)
    {
        JsonDocument document = ParseJson(line, path, lineNumber);
        using (document)
        {
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
            )
            {
                return type.GetString() ?? "null";
            }
        }

        throw new InvalidDataException(
            $"Fixture '{path}' line {lineNumber} is missing a string 'type' property."
        );
    }

    private static T Deserialize<T>(string line, string path, int lineNumber)
    {
        JsonDocument document = ParseJson(line, path, lineNumber);
        using (document)
        {
            return document.RootElement.Deserialize<T>(JsonOptions)
                ?? throw new InvalidDataException($"Fixture '{path}' line {lineNumber} is empty.");
        }
    }

    private static JsonDocument ParseJson(string line, string path, int lineNumber)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Fixture '{path}' line {lineNumber} is not valid JSON: {exception.Message}",
                exception
            );
        }
    }

    private sealed record HeaderLine(string? Type, int? Schema, string? Model, string? RecordedAt);

    private sealed record ExchangeLine(
        string? Type,
        string? RequestDigest,
        List<ChatResponseUpdate>? Updates
    );

    private sealed record DigestInput(
        List<ChatMessage> Messages,
        string? ModelId,
        List<string> ToolNames
    );

    /// <summary>The digest form of an image <see cref="DataContent"/>: identity, never bytes.</summary>
    internal sealed class ImageDigestContent(string mediaType, string digest) : AIContent
    {
        [JsonPropertyName("mediaType")]
        public string MediaType { get; } = mediaType;

        [JsonPropertyName("digest")]
        public string Digest { get; } = digest;
    }
}

internal sealed record FixtureHeader(int Schema, string Model, string RecordedAt);

internal sealed record FixtureExchange(
    string RequestDigest,
    IReadOnlyList<ChatResponseUpdate> Updates
);

internal sealed record FixtureDocument(
    FixtureHeader Header,
    IReadOnlyList<FixtureExchange> Exchanges
);
