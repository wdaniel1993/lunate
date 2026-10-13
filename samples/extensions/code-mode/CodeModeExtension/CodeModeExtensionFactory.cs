using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace CodeModeExtension;

/// <summary>
/// The one public factory the loader discovers in the entry assembly. It captures the core/process
/// service and registers the <c>run</c> tool (model-facing <c>codemode__run</c>): the handler runs
/// model-written JavaScript in a child process and bridges its tool calls back through
/// <see cref="ToolContext.ExecuteToolAsync"/>. The annotations are not read-only and
/// destructive-capable, so the approval policy asks before a run. Registration is registration
/// only: nothing runs until the model calls the tool.
/// </summary>
public sealed class CodeModeExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        CodeModeSettings settings = CodeModeSettings.From(context.Settings);
        if (!context.TryGetCoreService<IProcessRunner>("core/process", out IProcessRunner? runner))
        {
            context.Log.Error(
                "codemode: the host did not provide the core/process service; the run tool will refuse calls until it does."
            );
        }

        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = "run",
                Description =
                    "Runs a JavaScript program (Node.js) in a child process and returns its result. "
                    + "The script calls this run's tools through the `tools` object its prelude defines, "
                    + "for example `const text = await tools[\"read\"]({ path: \"a.txt\" })`; write progress "
                    + "with console.log and end with `return value` to make it the tool result. The process "
                    + "is killed on timeout and its output is capped.",
                ParametersSchema = Schema,
                Annotations = new ToolAnnotations(ReadOnly: false, Destructive: true),
                Handler = new CodeModeToolHandler(settings, runner),
            }
        );

        return new CodeModeExtension(context.Id);
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
                    "code": {
                      "type": "string",
                      "description": "the JavaScript to run; end with `return value` to make it the result"
                    },
                    "timeoutSeconds": {
                      "type": "integer",
                      "minimum": 1,
                      "maximum": 600,
                      "description": "the deadline in seconds; a call may only tighten the configured timeoutSeconds"
                    }
                  },
                  "required": ["code"],
                  "additionalProperties": false
                }
                """
            );
            return document.RootElement.Clone();
        }
    }
}

/// <summary>The extension instance.</summary>
public sealed class CodeModeExtension(string id) : IExtension
{
    public string Id { get; } = id;
}
