using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The JSON options for session payloads: a derived copy of
/// <see cref="AIJsonUtilities.DefaultOptions"/> with the session-only image reference content type
/// registered. Deriving from the default copy keeps every content type it already knows
/// serializing byte-for-byte identically; only the image part is added.
/// </summary>
internal static class SessionJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions);
        options.AddAIContentType<ImageContentReference>("image");
        // Freeze the instance: a late registration would otherwise fail mid-request with
        // "cannot modify after first use" instead of at the call site.
        options.MakeReadOnly();
        return options;
    }
}
