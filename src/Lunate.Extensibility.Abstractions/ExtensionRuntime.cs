using System.Globalization;
using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// The out-of-process runtime a remote extension declares in its manifest, mirroring the MCP
/// server registration shape: a command and arguments, an optional working directory, the two
/// environment halves (<see cref="Environment"/> values to set, <see cref="EnvironmentPassthrough"/>
/// names inherited from the parent), an optional per-call timeout and the startup mode.
/// </summary>
/// <param name="Command">The executable; a relative path resolves against the extension directory.</param>
/// <param name="Arguments">The arguments, verbatim; a relative first argument resolves against the extension directory.</param>
/// <param name="WorkingDirectory">The child's working directory; null uses the extension directory.</param>
/// <param name="Environment">Environment values to set on the child.</param>
/// <param name="EnvironmentPassthrough">Parent environment variable names to pass through.</param>
/// <param name="CallTimeout">The per-call timeout for host → child requests; null uses the default.</param>
/// <param name="Startup">The startup mode; null starts lazily, <c>background</c> starts after the UI is ready.</param>
public sealed record ExtensionRuntime(
    string Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyList<string>? EnvironmentPassthrough = null,
    TimeSpan? CallTimeout = null,
    string? Startup = null
)
{
    /// <summary>The one declared startup mode: start after the UI is ready, never on the startup path.</summary>
    public const string BackgroundStartup = "background";

    /// <summary>Whether this runtime starts in the background after the UI is ready.</summary>
    public bool StartsInBackground =>
        string.Equals(Startup, BackgroundStartup, StringComparison.Ordinal);

    /// <summary>Parses the manifest's <c>runtime</c> object; null when absent.</summary>
    internal static ExtensionRuntime? Parse(JsonElement root, string fileName)
    {
        if (!root.TryGetProperty("runtime", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"{fileName}: field 'runtime' must be an object mirroring the MCP server shape."
            );
        }

        string command = RequiredName(element, fileName, "runtime.command");
        return new ExtensionRuntime(
            command,
            StringArray(element, fileName, "runtime.args"),
            OptionalName(element, fileName, "runtime.workingDirectory"),
            ObjectMap(element, fileName, "runtime.env"),
            PassthroughArray(element, fileName, "runtime.envPassthrough"),
            ParseCallTimeout(element, fileName),
            ParseStartup(element, fileName)
        );
    }

    private static string RequiredName(JsonElement runtime, string fileName, string field)
    {
        if (
            !runtime.TryGetProperty(FieldName(field), out JsonElement element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(element.GetString())
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field '{field}' must be a non-empty string."
            );
        }

        return element.GetString()!;
    }

    private static string? OptionalName(JsonElement runtime, string fileName, string field)
    {
        if (!runtime.TryGetProperty(FieldName(field), out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(element.GetString()))
        {
            throw new InvalidDataException(
                $"{fileName}: field '{field}' must be a non-empty string."
            );
        }

        return element.GetString();
    }

    private static IReadOnlyList<string> StringArray(
        JsonElement runtime,
        string fileName,
        string field
    )
    {
        if (!runtime.TryGetProperty(FieldName(field), out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"{fileName}: field '{field}' must be an array of strings."
            );
        }

        List<string> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    $"{fileName}: field '{field}' must be an array of strings."
                );
            }

            values.Add(item.GetString()!);
        }

        return values;
    }

    private static IReadOnlyList<string>? PassthroughArray(
        JsonElement runtime,
        string fileName,
        string field
    )
    {
        List<string> values = [.. StringArray(runtime, fileName, field)];
        if (values.Count == 0 && !runtime.TryGetProperty(FieldName(field), out _))
        {
            return null;
        }

        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            string duplicate = values.First(value =>
                values.Count(item => string.Equals(item, value, StringComparison.Ordinal)) > 1
            );
            throw new InvalidDataException(
                $"{fileName}: field '{field}' declares '{duplicate}' more than once."
            );
        }

        foreach (string name in values)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException(
                    $"{fileName}: field '{field}' must name non-empty environment variables."
                );
            }
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string>? ObjectMap(
        JsonElement runtime,
        string fileName,
        string field
    )
    {
        if (!runtime.TryGetProperty(FieldName(field), out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"{fileName}: field '{field}' must be an object mapping names to strings."
            );
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    $"{fileName}: field '{field}' must map every name to a string."
                );
            }

            values[property.Name] = property.Value.GetString()!;
        }

        return values.Count == 0 ? null : values;
    }

    private static TimeSpan? ParseCallTimeout(JsonElement runtime, string fileName)
    {
        if (!runtime.TryGetProperty("callTimeout", out JsonElement element))
        {
            return null;
        }

        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out double seconds)
            || seconds <= 0
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field 'runtime.callTimeout' must be a positive number of seconds."
            );
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static string? ParseStartup(JsonElement runtime, string fileName)
    {
        if (!runtime.TryGetProperty("startup", out JsonElement element))
        {
            return null;
        }

        if (
            element.ValueKind != JsonValueKind.String
            || !string.Equals(element.GetString(), BackgroundStartup, StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field 'runtime.startup' must be '{BackgroundStartup}' when present."
            );
        }

        return BackgroundStartup;
    }

    private static string FieldName(string field) => field[(field.IndexOf('.') + 1)..];

    /// <summary>The startup mode in culture-independent form (the diagnostics read it).</summary>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"runtime '{Command}' ({Arguments.Count} args), startup={Startup ?? "lazy"}"
        );
}
