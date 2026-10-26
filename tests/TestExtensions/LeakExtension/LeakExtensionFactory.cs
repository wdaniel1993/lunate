using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace LeakExtension;

/// <summary>An extension that contributes one of everything, and nothing else.</summary>
public sealed class LeakExtension : IExtension;

/// <summary>
/// A fixture that registers one contribution of every kind an extension can make — hook, tool,
/// model provider, virtual model, tool-result renderer, status widget, MCP server, skill source,
/// command source, background service and file-change subscription — so a test can prove that a
/// failed or cancelled load leaves none of them behind. The last thing the factory does is log
/// <see cref="RegisteredMarker"/>, which a test's own log uses to cancel the load at a known point.
/// </summary>
public sealed class LeakExtensionFactory : IExtensionFactory
{
    /// <summary>The tool's local name; the manifest declares exactly this.</summary>
    public const string ToolName = "leak_probe";

    /// <summary>The command the source provides; the manifest declares exactly this.</summary>
    public const string CommandName = "leak-probe";

    /// <summary>The model id the programmatic provider serves.</summary>
    public const string ModelId = "leak-model";

    /// <summary>The last line the factory logs, after every registration.</summary>
    public const string RegisteredMarker = "leak registered everything";

    public IExtension Create(IExtensionContext context)
    {
        context.Register(new LeakSessionStartedHandler(context.Log));
        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = ToolName,
                Description = "A probe tool that exists only to be registered.",
                ParametersSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                Handler = new LeakToolHandler(),
            }
        );
        context.RegisterModelProvider(
            new ExtensionModelProvider("fixture", [ModelId], () => new LeakChatClient())
        );
        context.RegisterVirtualModel(new VirtualModelRegistration("auto", [ModelId], _ => ModelId));
        context.RegisterToolResultRenderer(new LeakRenderer());
        context.RegisterStatusWidget(new LeakWidget());
        context.RegisterMcpServer(
            new McpServerRegistration(
                "leak",
                new McpServerConfig("lunate-leak-fixture-command-that-cannot-exist", [])
            )
        );
        context.RegisterSkillSource(new LeakSkillSource());
        context.RegisterCommandSource(new LeakCommandSource(context.Id));
        context.RegisterService($"ext/{context.Id}/probe", new LeakBackgroundService(context.Log));
        context.SubscribeFileChanged(new LeakFileChangedHandler(context.Log));
        context.Log.Info(RegisteredMarker);
        return new LeakExtension();
    }

    private sealed class LeakSessionStartedHandler(IExtensionLog log) : ISessionStartedHandler
    {
        public int Priority => 0;

        public ValueTask HandleAsync(
            SessionStartedPayload payload,
            CancellationToken cancellationToken
        )
        {
            log.Info($"leak session started in {payload.WorkingDirectory}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LeakToolHandler : IExtensionToolHandler
    {
        public Task<ToolResult> InvokeAsync(
            JsonElement arguments,
            ToolContext context,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("leak probe", false));
    }

    private sealed class LeakRenderer : IToolResultRenderer
    {
        public string ToolName => LeakExtensionFactory.ToolName;

        public IReadOnlyList<TextSegment> Render(ToolResultView view) =>
            [new TextSegment("leak-rendered " + view.ResultText, TextStyle.Dim)];
    }

    private sealed class LeakWidget : IStatusWidget
    {
        public IReadOnlyList<TextSegment> Segments(StatusView view) =>
            [new TextSegment("leak-widget", TextStyle.Accent)];
    }

    private sealed class LeakSkillSource : ISkillSource
    {
        public string Id => "leak";

        public IReadOnlyList<string> SkillDirectories => [Path.Combine("skills", "leak-skill")];
    }

    private sealed class LeakCommandSource(string extensionId) : ICommandSource
    {
        public string SourceId => extensionId;

        public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
            CancellationToken cancellationToken
        )
        {
            // The cancellation window a test opens from its own log: the command cross-check is the
            // first place in the load that observes the token after every registration happened.
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<CommandDescriptor>>([
                new CommandDescriptor(
                    LeakExtensionFactory.CommandName,
                    "A probe command.",
                    null,
                    extensionId,
                    CommandKind.Extension
                ),
            ]);
        }

        public ValueTask<CommandResult> InvokeAsync(
            CommandInvocation invocation,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(CommandResult.Notice("leak probe command"));
    }

    private sealed class LeakBackgroundService(IExtensionLog log) : IBackgroundService
    {
        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            // The last point in a load that observes the token after every registration: a test
            // cancels from the line above, so the load ends with its state fully published.
            log.Info("leak service starting");
            cancellationToken.ThrowIfCancellationRequested();
            log.Info("leak service started");
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            log.Info("leak service stopped");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LeakFileChangedHandler(IExtensionLog log) : IFileChangedHandler
    {
        public int Priority => 0;

        public ValueTask OnFileChangedAsync(
            FileChangedPayload payload,
            CancellationToken cancellationToken
        )
        {
            log.Info($"leak file changed: {payload.Path}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LeakChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "leak reply"))
                {
                    ModelId = LeakExtensionFactory.ModelId,
                }
            );

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "leak reply")
            {
                ModelId = LeakExtensionFactory.ModelId,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
