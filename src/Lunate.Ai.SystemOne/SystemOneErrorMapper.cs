using System.Net;
using System.Text.Json;

namespace Lunate.Ai.SystemOne;

internal static class SystemOneErrorMapper
{
    public static DecisionTransportException Create(HttpStatusCode statusCode, string body)
    {
        string message = body;
        string? code = null;
        string? field = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            JsonElement error = FindErrorObject(root);
            message = StringProperty(error, "message") ?? StringProperty(root, "message") ?? body;
            code = StringProperty(error, "code");
            field = FindField(message);
        }
        catch (JsonException)
        {
            // Keep the provider body for non-JSON errors; it is the most useful diagnostic available.
        }

        if (
            statusCode == HttpStatusCode.BadRequest
            && string.Equals(code, "max_tokens_exceeded", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("max_tokens_exceeded", StringComparison.OrdinalIgnoreCase)
        )
        {
            message = $"max_tokens_exceeded: {message}";
        }

        return new DecisionTransportException(statusCode, message, field);
    }

    private static JsonElement FindErrorObject(JsonElement root)
    {
        if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
        {
            return error;
        }
        if (root.TryGetProperty("detail", out JsonElement detail) && detail.ValueKind == JsonValueKind.Object)
        {
            return detail;
        }
        return root;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? FindField(string message)
    {
        try
        {
            using JsonDocument nested = JsonDocument.Parse(message);
            JsonElement errors = nested.RootElement;
            if (errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                JsonElement first = errors[0];
                if (
                    first.TryGetProperty("path", out JsonElement path)
                    && path.ValueKind == JsonValueKind.Array
                )
                {
                    string[] parts = path
                        .EnumerateArray()
                        .Select(value => value.ValueKind switch
                        {
                            JsonValueKind.String => value.GetString(),
                            JsonValueKind.Number => value.GetRawText(),
                            _ => null,
                        })
                        .OfType<string>()
                        .ToArray();
                    return parts.Length == 0 ? null : string.Join('.', parts);
                }
            }
        }
        catch (JsonException)
        {
            // Most provider messages are plain text and do not include a structured path.
        }
        return null;
    }
}
