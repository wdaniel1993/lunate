using System.Text.Json;

namespace Lunate.Coding;

public static partial class SettingsStore
{
    /// <summary>
    /// Reads the user-scope container environment selection. The value has the same one accepted
    /// shape as <c>--env</c>, and anything else fails the file with both shapes named rather than
    /// starting a run the flag would have refused.
    /// </summary>
    private static ExecutionEnvironmentSelection? ReadEnvironment(
        JsonElement value,
        List<string> problems
    )
    {
        if (
            value.ValueKind == JsonValueKind.String
            && ExecutionEnvironmentSelection.TryParse(value.GetString(), out var selection)
        )
        {
            return selection;
        }

        problems.Add(
            $"'environment' must be {ExecutionEnvironmentSelection.AcceptedShapes}, but was "
                + (
                    value.ValueKind == JsonValueKind.String
                        ? $"'{value.GetString()}'"
                        : $"a {value.ValueKind} value"
                )
        );
        return null;
    }
}
