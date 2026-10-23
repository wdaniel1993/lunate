using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace CommandExtension;

public sealed class CommandExtension : IExtension;

/// <summary>
/// A fixture extension that registers one command source from its settings. Settings shape:
/// <c>{"commands":[{"name":"ping","description":"…","argumentHint":"…","behavior":"echo|throw"}]}</c>.
/// With no settings it registers no source, so its manifest commands read as declared, not
/// provided.
/// </summary>
public sealed class CommandExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        if (
            context.Settings.TryGet("commands", out JsonElement commands)
            && commands.ValueKind == JsonValueKind.Array
        )
        {
            List<CommandDescriptor> descriptors = [];
            foreach (JsonElement command in commands.EnumerateArray())
            {
                string name = ReadString(command, "name") ?? string.Empty;
                descriptors.Add(
                    new CommandDescriptor(
                        name,
                        ReadString(command, "description") ?? $"Command '{name}'.",
                        ReadString(command, "argumentHint"),
                        context.Id,
                        CommandKind.Extension
                    )
                );
            }

            context.RegisterCommandSource(
                new ScriptedCommandSource(
                    context.Id,
                    descriptors,
                    ReadBehavior(commands),
                    context.Log
                )
            );
        }

        return new CommandExtension();
    }

    private static string? ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string ReadBehavior(JsonElement commands) =>
        commands.GetArrayLength() > 0 ? ReadString(commands[0], "behavior") ?? "echo" : "echo";

    private sealed class ScriptedCommandSource(
        string id,
        IReadOnlyList<CommandDescriptor> descriptors,
        string behavior,
        IExtensionLog log
    ) : ICommandSource
    {
        public string SourceId => id;

        public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(descriptors);

        public ValueTask<CommandResult> InvokeAsync(
            CommandInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            log.Info(
                $"command {invocation.Name} invoked with '{invocation.Argument}' (source {id})"
            );
            return behavior switch
            {
                "throw" => throw new InvalidOperationException("the scripted command exploded"),
                _ => ValueTask.FromResult(
                    CommandResult.Notice($"{invocation.Name} {id}: {invocation.Argument}")
                ),
            };
        }
    }
}
