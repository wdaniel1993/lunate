using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using AgentToolExposure = Lunate.Agent.ToolExposure;

namespace ToolExtension;

public sealed class ToolExtension : IExtension;

public sealed class ToolExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        if (
            context.Settings.TryGet("tools", out JsonElement tools)
            && tools.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement tool in tools.EnumerateArray())
            {
                context.RegisterTool(ReadRegistration(tool));
            }
        }

        return new ToolExtension();
    }

    private static ExtensionToolRegistration ReadRegistration(JsonElement tool)
    {
        string name = ReadString(tool, "name") ?? string.Empty;
        return new ExtensionToolRegistration
        {
            Name = name,
            Description = ReadString(tool, "description") ?? $"Tool '{name}'.",
            ParametersSchema = ReadSchema(tool, "schema") ?? ObjectSchema(),
            OutputSchema = ReadOptionalSchema(tool, "outputSchema"),
            Annotations = ReadAnnotations(tool),
            Exposure = ReadExposure(tool),
            Risk = ReadRisk(tool),
            Handler = new ScriptedToolHandler(
                ReadString(tool, "behavior") ?? "echo",
                ReadString(tool, "marker")
            ),
        };
    }

    private static string? ReadString(JsonElement tool, string name) =>
        tool.ValueKind == JsonValueKind.Object
        && tool.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement? ReadSchema(JsonElement tool, string name) =>
        tool.ValueKind == JsonValueKind.Object && tool.TryGetProperty(name, out JsonElement value)
            ? value.Clone()
            : null;

    private static JsonElement? ReadOptionalSchema(JsonElement tool, string name) =>
        tool.ValueKind == JsonValueKind.Object
        && tool.TryGetProperty(name, out JsonElement value)
        && value.ValueKind != JsonValueKind.Null
            ? value.Clone()
            : null;

    private static ToolAnnotations? ReadAnnotations(JsonElement tool)
    {
        if (
            tool.ValueKind != JsonValueKind.Object
            || !tool.TryGetProperty("annotations", out JsonElement value)
            || value.ValueKind != JsonValueKind.Object
        )
        {
            return null;
        }

        return new ToolAnnotations(
            ReadBool(value, "readOnly"),
            ReadBool(value, "destructive"),
            ReadBool(value, "idempotent"),
            ReadBool(value, "openWorld")
        );
    }

    private static bool ReadBool(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.True;

    private static AgentToolExposure ReadExposure(JsonElement tool) =>
        Enum.TryParse(
            ReadString(tool, "exposure"),
            ignoreCase: true,
            out AgentToolExposure exposure
        )
            ? exposure
        : ReadString(tool, "exposure") is null ? AgentToolExposure.Direct
        : (AgentToolExposure)999;

    private static ToolRisk? ReadRisk(JsonElement tool) =>
        Enum.TryParse(ReadString(tool, "risk"), ignoreCase: true, out ToolRisk risk) ? risk : null;

    private static JsonElement ObjectSchema() =>
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
}

internal sealed class ScriptedToolHandler(string behavior, string? marker = null)
    : IExtensionToolHandler
{
    public async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    )
    {
        if (marker is not null)
        {
            File.WriteAllText(
                marker,
                FormattableString.Invariant(
                    $"invoked args={arguments.GetRawText()} cwd={context.WorkingDirectory} run={context.RunId ?? "none"} call={context.CallId ?? "none"}"
                )
            );
        }

        switch (behavior)
        {
            case "throw":
                throw new InvalidOperationException("the scripted tool exploded");
            case "block":
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return new ToolResult("unreachable", false);
            default:
                return new ToolResult(
                    FormattableString.Invariant(
                        $"echo: args={arguments.GetRawText()} cwd={context.WorkingDirectory} run={context.RunId ?? "none"} call={context.CallId ?? "none"}"
                    ),
                    false
                );
        }
    }
}
