using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

public interface IExtensionSettings
{
    bool TryGet(string path, out JsonElement value);
}
