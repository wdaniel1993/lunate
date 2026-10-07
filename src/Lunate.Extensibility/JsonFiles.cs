using System.Text.Json;

namespace Lunate.Extensibility;

internal static class JsonFiles
{
    public static JsonElement ReadObject(string path, string label)
    {
        if (!File.Exists(path))
        {
            using JsonDocument empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new ExtensionLoadException(
                $"{label} file '{path}' is not valid JSON: {exception.Message}",
                exception
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ExtensionLoadException(
                $"{label} file '{path}' could not be read: {exception.Message}",
                exception
            );
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ExtensionLoadException($"{label} file '{path}' must be a JSON object.");
            }

            return document.RootElement.Clone();
        }
    }
}
