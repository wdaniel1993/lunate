using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

internal static class HookSemanticsSupport
{
    public static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    public static ToolCallingPayload Payload(string path) =>
        new("r1", "c1", "read", Arguments(path));

    public static TurnEndedEntry Entry(string type) =>
        new(type, JsonDocument.Parse("{}").RootElement.Clone());

    public static JsonElement Arguments(string path) =>
        JsonDocument.Parse($$"""{"path":"{{path}}"}""").RootElement.Clone();
}
