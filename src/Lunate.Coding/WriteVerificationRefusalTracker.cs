using System.Text.Json;

namespace Lunate.Coding;

internal sealed class WriteVerificationRefusalTracker
{
    private readonly Dictionary<string, RefusalState> _refusals = new(
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase
    );

    internal bool TryGetBypass(string? runId, string canonicalPath, out JsonElement? diagnostics)
    {
        lock (_refusals)
        {
            if (_refusals.TryGetValue(Key(runId, canonicalPath), out RefusalState? state))
            {
                diagnostics = state.Diagnostics;
                return state.Count >= 2;
            }
        }

        diagnostics = null;
        return false;
    }

    internal void RecordRefusal(string? runId, string canonicalPath, JsonElement? diagnostics)
    {
        lock (_refusals)
        {
            string key = Key(runId, canonicalPath);
            _refusals.TryGetValue(key, out RefusalState? state);
            _refusals[key] = new RefusalState(
                (state?.Count ?? 0) + 1,
                diagnostics?.Clone() ?? state?.Diagnostics
            );
        }
    }

    private static string Key(string? runId, string canonicalPath) =>
        $"{runId ?? string.Empty}\0{canonicalPath}";

    private sealed record RefusalState(int Count, JsonElement? Diagnostics);
}
