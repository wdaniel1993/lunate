using System.Text;
using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// Reads and writes <c>~/.lunate/auth.json</c> (schema 1): named API keys for per-model
/// credential references. <see cref="Save"/> replaces the file atomically and restricts it to
/// owner-only permissions on POSIX. The provider preset's environment variable (and its alias)
/// overrides the corresponding provider entry when it is non-blank; custom named keys have no
/// environment override. Errors name keys, never values. The path and the environment lookup are
/// injectable.
/// </summary>
public sealed class AuthStore
{
    /// <summary>The only supported schema version.</summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly Func<string, string?> _environment;
    private readonly Dictionary<string, string> _keys;

    private AuthStore(
        string filePath,
        Func<string, string?> environment,
        Dictionary<string, string> keys
    )
    {
        _filePath = filePath;
        _environment = environment;
        _keys = keys;
    }

    /// <summary>The stored keys, without environment overrides applied.</summary>
    public IReadOnlyDictionary<string, string> Keys => _keys;

    /// <summary>Loads the store; a missing file yields an empty store, a malformed file one error listing every problem.</summary>
    public static AuthStore Load(string? filePath = null, Func<string, string?>? environment = null)
    {
        string path = filePath ?? DefaultFilePath();
        var env = environment ?? Environment.GetEnvironmentVariable;
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return new AuthStore(path, env, keys);
        }

        var problems = new List<string>();
        Parse(path, keys, problems);
        if (problems.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid auth file '{path}': {string.Join("; ", problems)}. No keys were loaded."
            );
        }

        return new AuthStore(path, env, keys);
    }

    /// <summary>Returns the named key with provider environment overrides applied, or null when it is missing or blank.</summary>
    public string? TryGet(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string? fromEnvironment = EnvironmentOverride(name);
        string? value = string.IsNullOrWhiteSpace(fromEnvironment)
            ? _keys.GetValueOrDefault(name)
            : fromEnvironment;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Returns the named key or throws an error that names the key but never a secret value.</summary>
    public string Require(string name) =>
        TryGet(name)
        ?? throw new InvalidOperationException(
            $"No API key named '{name}' is available in '{_filePath}'. Add it there or set the matching environment variable."
        );

    /// <summary>Sets a named key in memory; call <see cref="Save"/> to persist it.</summary>
    public void Set(string name, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        _keys[name] = secret;
    }

    /// <summary>Removes a named key in memory; returns whether it was present. Call <see cref="Save"/> to persist it.</summary>
    public bool Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _keys.Remove(name);
    }

    /// <summary>
    /// Writes the store atomically, creating the replacement owner-only on POSIX before the move so
    /// a secret is never briefly world-readable. A failed write leaves the old file intact.
    /// </summary>
    public void Save()
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string targetDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
        string temp = Path.Combine(targetDirectory, Path.GetRandomFileName() + ".tmp");
        var sorted = new SortedDictionary<string, string>(_keys, StringComparer.Ordinal);
        string json = JsonSerializer.Serialize(
            new AuthFile(SupportedSchemaVersion, sorted),
            SerializerOptions
        );

        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temp, options))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
            }

            File.Move(temp, _filePath, overwrite: true);

            // Best-effort on Windows: the ACL model has no direct rw------- equivalent here.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void Parse(string path, Dictionary<string, string> keys, List<string> problems)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            problems.Add($"the file is not valid JSON ({exception.Message})");
            return;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add("the root must be a JSON object");
                return;
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
                    case "keys":
                        ReadKeys(property.Value, keys, problems);
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
    }

    private static void ReadKeys(
        JsonElement element,
        Dictionary<string, string> keys,
        List<string> problems
    )
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'keys' must be an object mapping names to secrets");
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (
                property.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(property.Value.GetString())
            )
            {
                problems.Add($"key '{property.Name}' must be a non-empty string");
                continue;
            }

            keys[property.Name] = property.Value.GetString()!;
        }
    }

    private string? EnvironmentOverride(string name)
    {
        if (ProviderPresets.Find(name) is not { } preset)
        {
            return null;
        }

        string? value = _environment(preset.KeyVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return preset.KeyVariableAlias is { } alias ? _environment(alias) : null;
    }

    private static string DefaultFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "auth.json"
        );

    private sealed record AuthFile(int SchemaVersion, SortedDictionary<string, string> Keys);
}
