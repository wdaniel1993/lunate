using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace UiShowcaseExtension;

/// <summary>The extension instance; holds nothing beyond its id.</summary>
public sealed class UiShowcaseExtension(string id) : IExtension
{
    public string Id { get; } = id;
}

/// <summary>
/// The one public factory the loader discovers. It registers the read-only <c>bars</c> tool
/// (model-facing <c>uikit__bars</c>) whose result is the compact JSON echo of its integer values,
/// the result renderer that draws one accent bar line per value, and the status widget that draws
/// the context usage bar. Registration is registration only: nothing renders until the session
/// asks, and the contributions are presentation only — the model sees the raw JSON result.
/// </summary>
public sealed class UiShowcaseExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = "bars",
                Description =
                    "Echoes a list of integer bar values as compact JSON; the interactive session renders them as bars in the scrollback.",
                ParametersSchema = Schema,
                Annotations = new ToolAnnotations(ReadOnly: true),
                Handler = new BarsHandler(),
            }
        );
        context.RegisterToolResultRenderer(new UiShowcaseBarsRenderer());
        context.RegisterStatusWidget(new UiShowcaseUsageWidget());
        context.Log.Info(
            "ui-showcase: registered the bars tool (model-facing uikit__bars), its result renderer and the usage widget"
        );

        return new UiShowcaseExtension(context.Id);
    }

    private static JsonElement Schema
    {
        get
        {
            using JsonDocument document = JsonDocument.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "values": {
                      "type": "array",
                      "items": { "type": "integer" },
                      "description": "the bar values to read back, one bar per value"
                    }
                  },
                  "required": ["values"],
                  "additionalProperties": false
                }
                """
            );
            return document.RootElement.Clone();
        }
    }

    /// <summary>The read-only tool: validates the arguments and returns the compact JSON result.</summary>
    private sealed class BarsHandler : IExtensionToolHandler
    {
        public Task<ToolResult> InvokeAsync(
            JsonElement arguments,
            ToolContext context,
            CancellationToken ct
        )
        {
            if (
                arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty("values", out JsonElement element)
                || element.ValueKind != JsonValueKind.Array
            )
            {
                return Task.FromResult(
                    new ToolResult(
                        "'values' is required and must be an array of integers.",
                        IsError: true
                    )
                );
            }

            var values = new List<int>();
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int value))
                {
                    return Task.FromResult(
                        new ToolResult("every 'values' entry must be an integer.", IsError: true)
                    );
                }

                values.Add(value);
            }

            return Task.FromResult(
                new ToolResult(JsonSerializer.Serialize(new { values }), IsError: false)
            );
        }
    }
}
