using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

internal static class FixtureFormat
{
    internal const int SchemaVersion = 1;

    private const string HeaderType = "header";
    private const string ExchangeType = "exchange";

    internal static JsonSerializerOptions JsonOptions { get; } =
        new(AIJsonUtilities.DefaultOptions)
        {
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

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
    /// not invalidate a fixture (revisit if it bites).
    /// </summary>
    internal static string ComputeRequestDigest(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options
    )
    {
        var input = new DigestInput(
            [.. messages],
            options?.ModelId,
            options?.Tools is { } tools ? [.. tools.Select(static tool => tool.Name)] : []
        );
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }

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
