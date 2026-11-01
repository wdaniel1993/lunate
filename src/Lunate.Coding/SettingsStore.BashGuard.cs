using System.Text.Json;

namespace Lunate.Coding;

public static partial class SettingsStore
{
    /// <summary>Reads the file-only guard setting; a missing key keeps the secure default.</summary>
    private static bool ReadBashGuard(JsonElement value, List<string> problems)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        problems.Add("'bashGuard' must be true or false");
        return true;
    }
}
