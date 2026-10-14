using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace CodeModeExtension;

/// <summary>The extension's settings: runtime, timeout, output cap and tool-call cap.</summary>
public sealed record CodeModeSettings(
    string Runtime,
    int TimeoutSeconds,
    int MaxOutputBytes,
    int MaxToolCalls
)
{
    public const string DefaultRuntime = "node";
    public const int DefaultTimeoutSeconds = 30;
    public const int DefaultMaxOutputBytes = 65536;
    public const int DefaultMaxToolCalls = 100;

    /// <summary>The configured timeout never exceeds this; a call's own value clamps into it.</summary>
    public const int MaxTimeoutSeconds = 600;

    public static CodeModeSettings From(IExtensionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string runtime = ReadString(settings, "runtime") is { Length: > 0 } configured
            ? configured
            : DefaultRuntime;
        int timeout = ReadInt(settings, "timeoutSeconds") is { } seconds
            ? Math.Clamp(seconds, 1, MaxTimeoutSeconds)
            : DefaultTimeoutSeconds;
        int maxOutputBytes = ReadInt(settings, "maxOutputBytes") is { } bytes and > 0
            ? bytes
            : DefaultMaxOutputBytes;
        int maxToolCalls = ReadInt(settings, "maxToolCalls") is { } calls and > 0
            ? calls
            : DefaultMaxToolCalls;
        return new CodeModeSettings(runtime, timeout, maxOutputBytes, maxToolCalls);
    }

    /// <summary>Resolves the effective deadline for one call: the call may only tighten the setting.</summary>
    public int ClampTimeout(int requestedSeconds) =>
        Math.Min(Math.Clamp(requestedSeconds, 1, MaxTimeoutSeconds), TimeoutSeconds);

    private static string? ReadString(IExtensionSettings settings, string name) =>
        settings.TryGet(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(IExtensionSettings settings, string name) =>
        settings.TryGet(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int parsed)
            ? parsed
            : null;
}
