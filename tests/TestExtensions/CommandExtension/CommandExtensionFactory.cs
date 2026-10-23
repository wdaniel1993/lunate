using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace CommandExtension;

public sealed class CommandExtension : IExtension;

/// <summary>
/// A fixture extension that registers one command source from its settings. Settings shape:
/// <c>{"commands":[{"name":"ping","description":"…","argumentHint":"…","behavior":"echo|throw|block|ignore|late"}],"sources":1}</c>.
/// <c>sources</c> registers that many sources with the same descriptors (the duplicate-provider
/// path); <c>behavior</c> scripts the handler. With no settings it registers no source, so its
/// manifest commands read as declared, not provided.
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

            string behavior = ReadBehavior(commands);
            int delayMs = ReadDelay(commands);
            int sources = ReadSources(context.Settings);
            for (var index = 0; index < sources; index++)
            {
                string sourceId =
                    index == 0
                        ? context.Id
                        : string.Create(CultureInfo.InvariantCulture, $"{context.Id}-{index + 1}");
                context.RegisterCommandSource(
                    new ScriptedCommandSource(
                        sourceId,
                        descriptors,
                        behavior,
                        delayMs,
                        context,
                        context.Log
                    )
                );
            }
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

    private static int ReadDelay(JsonElement commands) =>
        commands.GetArrayLength() > 0
        && commands[0].TryGetProperty("delayMs", out JsonElement delay)
        && delay.ValueKind == JsonValueKind.Number
            ? delay.GetInt32()
            : 2000;

    private static int ReadSources(IExtensionSettings settings) =>
        settings.TryGet("sources", out JsonElement sources)
        && sources.ValueKind == JsonValueKind.Number
            ? Math.Max(1, sources.GetInt32())
            : 1;

    private sealed class ScriptedCommandSource(
        string id,
        IReadOnlyList<CommandDescriptor> descriptors,
        string behavior,
        int delayMs,
        IExtensionContext context,
        IExtensionLog log
    ) : ICommandSource
    {
        public string SourceId => id;

        public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(descriptors);

        public async ValueTask<CommandResult> InvokeAsync(
            CommandInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            log.Info(
                $"command {invocation.Name} invoked with '{invocation.Argument}' (source {id})"
            );
            switch (behavior)
            {
                case "throw":
                    throw new InvalidOperationException("the scripted command exploded");
                case "block":
                    // Obeys the token: the unload's cancellation reaches it and it returns.
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    break;
                case "ignore":
                    // Swallows the token: the unload's drain must time out, not wait forever.
                    await Task.Delay(delayMs, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "late":
                    // Swallows the token, then calls the host after the unload: the disposed
                    // context throws, and the host contains it.
                    await Task.Delay(delayMs, CancellationToken.None).ConfigureAwait(false);
                    context.TryGetService("core/process", out _);
                    break;
            }

            return CommandResult.Notice($"{invocation.Name} {id}: {invocation.Argument}");
        }
    }
}
