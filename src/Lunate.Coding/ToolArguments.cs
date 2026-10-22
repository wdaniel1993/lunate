using System.Text.Json;

namespace Lunate.Coding;

/// <summary>
/// The hand-written argument checks shared by the optional search tools: an object root, one
/// unknown-key error naming every accepted argument, and typed reads with teaching errors.
/// </summary>
internal static class ToolArguments
{
    /// <summary>
    /// Checks the root is an object and every key is accepted. On failure the error names the
    /// accepted arguments so the model can retry without another request.
    /// </summary>
    internal static bool TryValidate(
        JsonElement args,
        IReadOnlyList<string> accepted,
        out string? error
    )
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            error = "arguments must be a JSON object";
            return false;
        }

        List<string> unknown = [];
        foreach (JsonProperty property in args.EnumerateObject())
        {
            if (!accepted.Contains(property.Name, StringComparer.Ordinal))
            {
                unknown.Add($"'{property.Name}'");
            }
        }

        if (unknown.Count > 0)
        {
            error =
                $"unknown argument{(unknown.Count == 1 ? string.Empty : "s")} "
                + $"{string.Join(", ", unknown)}; accepted: {string.Join(", ", accepted)}";
            return false;
        }

        error = null;
        return true;
    }

    internal static bool TryString(
        JsonElement args,
        string name,
        out string? value,
        out string? error
    )
    {
        value = null;
        error = null;
        if (!args.TryGetProperty(name, out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"{name} must be a string";
            return false;
        }

        value = element.GetString();
        return true;
    }

    internal static bool TryBoolean(
        JsonElement args,
        string name,
        out bool? value,
        out string? error
    )
    {
        value = null;
        error = null;
        if (!args.TryGetProperty(name, out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = $"{name} must be true or false";
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    internal static bool TryInteger(
        JsonElement args,
        string name,
        out int? value,
        out string? error
    )
    {
        value = null;
        error = null;
        if (!args.TryGetProperty(name, out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int parsed))
        {
            error = $"{name} must be an integer";
            return false;
        }

        value = parsed;
        return true;
    }
}
