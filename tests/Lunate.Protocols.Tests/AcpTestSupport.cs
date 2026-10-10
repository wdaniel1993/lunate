using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Acp;
using Acp.JsonRpc;
using Acp.Schema;
using Acp.Streaming;
using Lunate.Agent;
using Lunate.Protocols.Acp;
using Microsoft.Extensions.AI;

namespace Lunate.Protocols.Tests;

/// <summary>
/// An in-process ACP runtime: <see cref="LibAcpServer"/> on one end of a pipe pair, LibAcp's
/// <see cref="ClientSideConnection"/> on the other (guide §Tests).
/// </summary>
internal static class AcpTestSupport
{
    public static AgentHarness Harness(IChatClient client, params ITool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (ITool tool in tools)
        {
            registry.Add(tool);
        }

        return new AgentHarness(client, registry, new AgentHarnessOptions { MaxRetries = 0 });
    }

    public static AcpRuntime Start(
        Func<AcpSessionContext, AgentHarness> createHarness,
        Action<string>? log = null
    )
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var state = new RecordingClient();
        Task serverTask = Task.Run(() =>
            new LibAcpServer(log).RunAsync(
                toServer.Reader.AsStream(),
                toClient.Writer.AsStream(),
                createHarness,
                CancellationToken.None
            )
        );
        var client = new ClientSideConnection(
            _ => state,
            new NdJsonStream(toClient.Reader.AsStream(), toServer.Writer.AsStream())
        );
        return new AcpRuntime(client, state, serverTask, toServer);
    }

    /// <summary>
    /// A bare agent-side connection for tests that build the client-backed approver or file access
    /// directly with short injected timeouts: the client end answers through a
    /// <see cref="RecordingClient"/> and leaves requests unanswered on demand.
    /// </summary>
    public static RawAcpRuntime StartRaw()
    {
        var toClient = new Pipe();
        var toServer = new Pipe();
        var state = new RecordingClient();
        var server = new AgentSideConnection(
            _ => new SilentAgent(),
            new NdJsonStream(toServer.Reader.AsStream(), toClient.Writer.AsStream())
        );
        var client = new ClientSideConnection(
            _ => state,
            new NdJsonStream(toClient.Reader.AsStream(), toServer.Writer.AsStream())
        );
        return new RawAcpRuntime(server, client, state, toClient);
    }

    public static InitializeRequest InitializeRequest =>
        new()
        {
            ProtocolVersion = Protocol.Version,
            ClientInfo = new Implementation { Name = "test-client", Version = "0.0.1" },
            ClientCapabilities = new ClientCapabilities(),
        };

    public static InitializeRequest InitializeRequestWithFileSystem =>
        new()
        {
            ProtocolVersion = Protocol.Version,
            ClientInfo = new Implementation { Name = "test-client", Version = "0.0.1" },
            ClientCapabilities = new ClientCapabilities
            {
                Fs = new FileSystemCapabilities { ReadTextFile = true, WriteTextFile = true },
            },
        };

    public static string Wire(SessionUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return JsonSerializer.Serialize(update, AcpJson.Options);
    }
}

internal sealed class AcpRuntime(
    ClientSideConnection client,
    RecordingClient state,
    Task serverTask,
    Pipe toServer
) : IAsyncDisposable
{
    public ClientSideConnection Client { get; } = client;

    public RecordingClient State { get; } = state;

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);
        await toServer.Writer.CompleteAsync().ConfigureAwait(false);
        await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
    }
}

/// <summary>A bare agent-side connection pair whose client end is a <see cref="RecordingClient"/>.</summary>
internal sealed class RawAcpRuntime(
    AgentSideConnection server,
    ClientSideConnection client,
    RecordingClient state,
    Pipe toClient
) : IAsyncDisposable
{
    public AgentSideConnection Server { get; } = server;

    public ClientSideConnection Client { get; } = client;

    public RecordingClient State { get; } = state;

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);
        await Server.DisposeAsync().ConfigureAwait(false);
        await toClient.Writer.CompleteAsync().ConfigureAwait(false);
    }
}

/// <summary>An agent stub for the raw connection: it never receives inbound requests.</summary>
internal sealed class SilentAgent : IAgent
{
    public Task<InitializeResponse> InitializeAsync(
        InitializeRequest request,
        CancellationToken cancellationToken
    ) => throw RequestErrorException.MethodNotFound(AgentMethods.Initialize);

    public Task<AuthenticateResponse?> AuthenticateAsync(
        AuthenticateRequest request,
        CancellationToken cancellationToken
    ) => throw RequestErrorException.MethodNotFound(AgentMethods.Authenticate);

    public Task<NewSessionResponse> NewSessionAsync(
        NewSessionRequest request,
        CancellationToken cancellationToken
    ) => throw RequestErrorException.MethodNotFound(AgentMethods.SessionNew);

    public Task<PromptResponse> PromptAsync(
        PromptRequest request,
        CancellationToken cancellationToken
    ) => throw RequestErrorException.MethodNotFound(AgentMethods.SessionPrompt);

    public Task CancelAsync(CancelNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>Records the <c>session/update</c> notifications the server sends and answers the
/// agent's outbound requests (permissions, file system) with deterministic handlers.</summary>
internal sealed class RecordingClient : IClient
{
    private readonly List<SessionNotification> updates = [];
    private readonly List<RequestPermissionRequest> permissionRequests = [];
    private readonly List<ReadTextFileRequest> fileReads = [];
    private readonly List<WriteTextFileRequest> fileWrites = [];
    private readonly SemaphoreSlim signals = new(0);
    private TaskCompletionSource<ReadTextFileResponse>? heldRead;
    private TaskCompletionSource<RequestPermissionResponse>? heldPermission;

    public int UpdateCount
    {
        get
        {
            lock (updates)
            {
                return updates.Count;
            }
        }
    }

    public IReadOnlyList<SessionNotification> Updates
    {
        get
        {
            lock (updates)
            {
                return [.. updates];
            }
        }
    }

    public IReadOnlyList<RequestPermissionRequest> PermissionRequests
    {
        get
        {
            lock (permissionRequests)
            {
                return [.. permissionRequests];
            }
        }
    }

    public IReadOnlyList<ReadTextFileRequest> FileReads
    {
        get
        {
            lock (fileReads)
            {
                return [.. fileReads];
            }
        }
    }

    public IReadOnlyList<WriteTextFileRequest> FileWrites
    {
        get
        {
            lock (fileWrites)
            {
                return [.. fileWrites];
            }
        }
    }

    /// <summary>Answers <c>session/request_permission</c>; the default cancels the request.</summary>
    public Func<
        RequestPermissionRequest,
        CancellationToken,
        Task<RequestPermissionResponse>
    >? PermissionHandler { get; set; }

    /// <summary>When true, every <c>session/request_permission</c> is left unanswered.</summary>
    public bool IgnorePermissions { get; set; }

    /// <summary>Serves <c>fs/read_text_file</c> content; null answers with resource-not-found.</summary>
    public Func<ReadTextFileRequest, string>? FileReadHandler { get; set; }

    /// <summary>When true, every <c>fs/read_text_file</c> request is left unanswered.</summary>
    public bool IgnoreFileReads { get; set; }

    /// <summary>
    /// Fails the read request currently held by <see cref="IgnoreFileReads"/> — the late answer a
    /// client sends for a request the agent already abandoned; false when none is held.
    /// </summary>
    public bool FailHeldRead(Exception error)
    {
        TaskCompletionSource<ReadTextFileResponse>? held;
        lock (fileReads)
        {
            held = heldRead;
        }

        return held is not null && held.TrySetException(error);
    }

    /// <summary>
    /// Fails the permission request currently held by <see cref="IgnorePermissions"/> — the late
    /// answer a client sends for a request the agent already abandoned; false when none is held.
    /// </summary>
    public bool FailHeldPermission(Exception error)
    {
        TaskCompletionSource<RequestPermissionResponse>? held;
        lock (permissionRequests)
        {
            held = heldPermission;
        }

        return held is not null && held.TrySetException(error);
    }

    /// <summary>When set, <c>fs/read_text_file</c> answers with this error.</summary>
    public Exception? FileReadError { get; set; }

    public Task SessionUpdateAsync(
        SessionNotification notification,
        CancellationToken cancellationToken
    )
    {
        lock (updates)
        {
            updates.Add(notification);
        }

        signals.Release();
        return Task.CompletedTask;
    }

    public Task<RequestPermissionResponse> RequestPermissionAsync(
        RequestPermissionRequest request,
        CancellationToken cancellationToken
    )
    {
        lock (permissionRequests)
        {
            permissionRequests.Add(request);
        }

        if (IgnorePermissions)
        {
            var held = new TaskCompletionSource<RequestPermissionResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            lock (permissionRequests)
            {
                heldPermission = held;
            }

            return held.Task;
        }

        return PermissionHandler is { } handler
            ? handler(request, cancellationToken)
            : Task.FromResult(
                new RequestPermissionResponse { Outcome = new CancelledPermissionOutcome() }
            );
    }

    public Task<ReadTextFileResponse> ReadTextFileAsync(
        ReadTextFileRequest request,
        CancellationToken cancellationToken
    )
    {
        lock (fileReads)
        {
            fileReads.Add(request);
        }

        signals.Release();
        if (IgnoreFileReads)
        {
            var held = new TaskCompletionSource<ReadTextFileResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            lock (fileReads)
            {
                heldRead = held;
            }

            return held.Task;
        }

        if (FileReadError is { } error)
        {
            return Task.FromException<ReadTextFileResponse>(error);
        }

        return FileReadHandler is { } handler
            ? Task.FromResult(new ReadTextFileResponse { Content = handler(request) })
            : Task.FromException<ReadTextFileResponse>(
                RequestErrorException.ResourceNotFound(request.Path)
            );
    }

    public Task<WriteTextFileResponse?> WriteTextFileAsync(
        WriteTextFileRequest request,
        CancellationToken cancellationToken
    )
    {
        lock (fileWrites)
        {
            fileWrites.Add(request);
        }

        return Task.FromResult<WriteTextFileResponse?>(new WriteTextFileResponse());
    }

    public async Task WaitForUpdatesAsync(int count, TimeSpan timeout, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (UpdateCount < count)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (
                remaining <= TimeSpan.Zero
                || !await signals.WaitAsync(remaining, ct).ConfigureAwait(false)
            )
            {
                throw new TimeoutException(
                    $"Expected {count} session/update notifications, saw {UpdateCount}."
                );
            }
        }
    }

    public async Task WaitForFileReadsAsync(int count, TimeSpan timeout, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (FileReads.Count < count)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (
                remaining <= TimeSpan.Zero
                || !await signals.WaitAsync(remaining, ct).ConfigureAwait(false)
            )
            {
                throw new TimeoutException(
                    $"Expected {count} fs/read_text_file requests, saw {FileReads.Count}."
                );
            }
        }
    }
}

/// <summary>A scripted provider: one queued update list per model call, requests recorded.</summary>
internal sealed class AcpScriptedChatClient : IChatClient
{
    private readonly Queue<ChatResponseUpdate[]> scripts = new();

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    /// <summary>Parks (observing cancellation) instead of failing when no script is left.</summary>
    public bool ParkWhenExhausted { get; set; }

    public string LastUserText => Requests[^1].Last(message => message.Role == ChatRole.User).Text;

    public AcpScriptedChatClient Enqueue(params ChatResponseUpdate[] updates)
    {
        scripts.Enqueue(updates);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Dequeue().ToChatResponse());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Requests.Add([.. messages]);
        if (scripts.Count == 0 && ParkWhenExhausted)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        ChatResponseUpdate[] updates = Dequeue();
        foreach (ChatResponseUpdate update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private ChatResponseUpdate[] Dequeue() =>
        scripts.Count > 0
            ? scripts.Dequeue()
            : throw new InvalidOperationException(
                "AcpScriptedChatClient has no scripted response left."
            );
}

/// <summary>Streams one chunk, then parks until the run is cancelled.</summary>
internal sealed class AcpGatedChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new ChatResponse());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        yield return new ChatResponseUpdate(
            ChatRole.Assistant,
            [new Microsoft.Extensions.AI.TextContent("Partial")]
        );
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>Parks inside <see cref="ExecuteAsync"/> until the run's token is cancelled.</summary>
internal sealed class GatedTool : ITool
{
    private readonly TaskCompletionSource started = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public string Name => "gate";

    public string Description => "Parks until the run is cancelled.";

    public JsonElement ParametersSchema { get; } =
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public Task Started => started.Task;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return new ToolResult("unreachable", IsError: false);
    }
}

internal sealed class EchoTool : ITool
{
    public string Name => "echo";

    public string Description => "Echoes the message argument.";

    public JsonElement ParametersSchema { get; } =
        JsonSerializer.SerializeToElement(
            new
            {
                type = "object",
                properties = new { message = new { type = "string" } },
                required = new[] { "message" },
            }
        );

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct) =>
        Task.FromResult(
            new ToolResult("echo: " + args.GetProperty("message").GetString(), IsError: false)
        );
}

internal static class AcpScripts
{
    public static ChatResponseUpdate Text(string text) =>
        new(ChatRole.Assistant, [new Microsoft.Extensions.AI.TextContent(text)]);

    public static ChatResponseUpdate Call(
        string callId,
        string name,
        IDictionary<string, object?> arguments
    ) => new(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]);

    public static ChatResponseUpdate ToolCalls() =>
        new(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.ToolCalls };

    public static ChatResponseUpdate Stop() =>
        new(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Stop };
}
