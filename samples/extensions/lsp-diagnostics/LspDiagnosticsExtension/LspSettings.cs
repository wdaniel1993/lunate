using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace LspDiagnosticsExtension;

/// <summary>The extension's settings with the documented defaults applied.</summary>
public sealed record LspSettings(
    string Command,
    IReadOnlyList<string> Args,
    TimeSpan Timeout,
    int MaxDiagnostics
)
{
    /// <summary>The default whole round trip budget; strictly under the runner's 5 s per-handler timeout.</summary>
    public const int DefaultTimeoutMs = 2000;

    /// <summary>The default cap on errors and warnings attached to one result.</summary>
    public const int DefaultMaxDiagnostics = 5;

    public static LspSettings From(IExtensionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string command = "csharp-ls";
        if (
            settings.TryGet("serverCommand", out JsonElement commandElement)
            && commandElement.ValueKind == JsonValueKind.String
            && commandElement.GetString() is { Length: > 0 } configured
        )
        {
            command = configured;
        }

        List<string> args = [];
        if (
            settings.TryGet("serverArgs", out JsonElement argsElement)
            && argsElement.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement arg in argsElement.EnumerateArray())
            {
                if (arg.ValueKind == JsonValueKind.String)
                {
                    args.Add(arg.GetString()!);
                }
            }
        }

        return new LspSettings(
            command,
            args,
            TimeSpan.FromMilliseconds(ReadInt(settings, "timeoutMs", DefaultTimeoutMs, minimum: 1)),
            ReadInt(settings, "maxDiagnostics", DefaultMaxDiagnostics, minimum: 0)
        );
    }

    private static int ReadInt(IExtensionSettings settings, string name, int fallback, int minimum)
    {
        if (
            settings.TryGet(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double number)
            && double.IsFinite(number)
        )
        {
            return (int)Math.Clamp(number, minimum, (double)int.MaxValue);
        }

        return fallback;
    }
}
