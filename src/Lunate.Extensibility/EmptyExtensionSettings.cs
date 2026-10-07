using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

internal sealed class EmptyExtensionSettings : IExtensionSettings
{
    public bool TryGet(string path, out JsonElement value)
    {
        value = default;
        return false;
    }
}
