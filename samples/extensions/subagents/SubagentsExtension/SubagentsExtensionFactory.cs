using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace SubagentsExtension;

/// <summary>
/// The one public factory the loader discovers in the entry assembly. It registers the
/// <c>spawn</c> tool (model-facing <c>subagents__spawn</c>): the handler runs a child agent through
/// <see cref="Lunate.Agent.ToolContext.Subagents"/>. The annotations are not read-only and
/// destructive-capable, so the approval policy asks before a spawn. Registration is registration
/// only: nothing runs until the model calls the tool.
/// </summary>
public sealed class SubagentsExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        SubagentsSettings settings = SubagentsSettings.From(context.Settings);
        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = "spawn",
                Description = settings.ResolvedDescription,
                ParametersSchema = Schema,
                Annotations = new Lunate.Agent.ToolAnnotations(ReadOnly: false, Destructive: true),
                Handler = new SpawnToolHandler(settings),
            }
        );

        return new SubagentsExtension(context.Id);
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
                    "prompt": {
                      "type": "string",
                      "description": "the task for the subagent"
                    },
                    "maxSteps": {
                      "type": "integer",
                      "minimum": 1,
                      "description": "the subagent's step ceiling; never loosens the run's own ceiling"
                    },
                    "tools": {
                      "type": "array",
                      "items": { "type": "string" },
                      "description": "the model-facing names of the tools to offer the subagent; unknown names refuse the spawn, omitted offers the run's full set"
                    }
                  },
                  "required": ["prompt"],
                  "additionalProperties": false
                }
                """
            );
            return document.RootElement.Clone();
        }
    }
}

/// <summary>The extension instance.</summary>
public sealed class SubagentsExtension(string id) : IExtension
{
    public string Id { get; } = id;
}
