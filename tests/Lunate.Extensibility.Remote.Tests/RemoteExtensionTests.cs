using System.Text.Json;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// The remote extension adapter: declaration-driven proxies without a start, lazy start and the
/// handshake contract, hook forwarding rules, per-class failure policies through the real hook
/// runner, service bridging both ways, every call carrying run ids and the workspace, and the
/// crash/restart/shutdown lifecycle.
/// </summary>
public sealed class RemoteExtensionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly WorkspaceInfo Workspace = new("/work", "/repo", "/repo/.git");
    private static JsonElement EmptyArgs => JsonDocument.Parse("{}").RootElement.Clone();

    [Fact]
    public void Proxies_register_without_starting_the_process()
    {
        Harness harness = Harness.Create(
            hooks: ["tool-calling", "session-started", "context-building"]
        );

        Assert.False(harness.Child.Started);
        Assert.Contains(harness.Context.Handlers, handler => handler is IToolCallingHandler);
        Assert.Contains(harness.Context.Handlers, handler => handler is ISessionStartedHandler);
        Assert.Contains(harness.Context.Handlers, handler => handler is IContextBuildingHandler);
        Assert.DoesNotContain(
            harness.Context.Handlers,
            handler => handler is IProviderStreamEventHandler
        );
    }

    [Fact]
    public async Task An_observer_only_manifest_subscribes_nothing_else()
    {
        Harness harness = Harness.Create(hooks: ["provider-stream-event"]);
        var handler = (IProviderStreamEventHandler)Assert.Single(harness.Context.Handlers);

        await handler.ObserveAsync(new ProviderStreamEventPayload("r1", "m", "x", null), Ct);

        Assert.Equal(1, harness.Child.StartCount);
        await WaitAsync(
            () =>
                harness.Child.Notifications.Any(notification =>
                    notification.Method == "hook/ProviderStreamEvent"
                ),
            "the notification arrived"
        );
        Assert.DoesNotContain(
            harness.Child.Notifications,
            n => n.Method.StartsWith("hook/Session")
        );
    }

    [Fact]
    public async Task A_hook_call_starts_the_child_lazily_and_the_result_lands()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);
        harness.Child.HookAnswers = (_, _, _) =>
            ValueTask.FromResult<JsonElement?>(
                HookResultCodec.EncodeResult(
                    HookContracts.Find("ToolCalling")!,
                    new ToolCallingResult.Block("not in this workspace")
                )
            );

        ToolCallingResult result = await handler.HandleAsync(
            new ToolCallingPayload("r1", "c1", "read", EmptyArgs),
            Ct
        );

        Assert.Equal(new ToolCallingResult.Block("not in this workspace"), result);
        Assert.Equal(1, harness.Child.StartCount);
    }

    [Fact]
    public async Task The_handshake_carries_version_capabilities_echo_workspace_and_surface()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);

        await handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct);

        InitializeRequest handshake = harness.Child.LastHandshake!;
        Assert.Equal(RemoteProtocol.Version, handshake.ProtocolVersion);
        Assert.Equal(RemoteProtocol.Capabilities, handshake.Capabilities);
        Assert.Equal("remote-sample", handshake.Manifest.GetProperty("id").GetString());
        Assert.Equal(Workspace, handshake.Workspace);
        Assert.Equal(["ToolCalling"], handshake.Hooks);
    }

    [Fact]
    public async Task Every_call_carries_run_ids_and_the_workspace()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);
        HookCallEnvelope? observed = null;
        harness.Child.HookAnswers = (_, parameters, _) =>
        {
            observed = parameters!.Value.Deserialize<HookCallEnvelope>(ScriptedChild.Options);
            return ValueTask.FromResult<JsonElement?>(
                HookResultCodec.EncodeResult(
                    HookContracts.Find("ToolCalling")!,
                    new ToolCallingResult.Proceed(null)
                )
            );
        };

        await handler.HandleAsync(
            new ToolCallingPayload("run-1", "c1", "read", EmptyArgs) { ParentRunId = "parent-1" },
            Ct
        );

        Assert.NotNull(observed);
        Assert.Equal("run-1", observed!.Call.RunId);
        Assert.Equal("parent-1", observed.Call.ParentRunId);
        Assert.Equal(Workspace, observed.Call.Workspace);
    }

    [Fact]
    public async Task A_protocol_version_mismatch_refuses_and_the_session_continues()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        harness.Child.Handshake = request => new InitializeResult(
            2,
            request.Hooks,
            request.Services
        );
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);

        await Assert.ThrowsAsync<RemoteExtensionException>(() =>
            handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct).AsTask()
        );

        Assert.Equal(RemoteExtensionState.Refused, harness.Extension.Status.State);
        Assert.Contains("was refused", harness.Context.Log.Errors[^1], StringComparison.Ordinal);
        Assert.Contains("protocol 2", harness.Extension.Status.Reason!, StringComparison.Ordinal);
        // Never driven best-effort: the next call refuses without touching the child again.
        await Assert.ThrowsAsync<RemoteExtensionException>(() =>
            handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct).AsTask()
        );
        Assert.Equal(1, harness.Child.StartCount);
    }

    [Fact]
    public async Task An_undeclared_implemented_hook_is_refused()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        harness.Child.Handshake = request => new InitializeResult(
            request.ProtocolVersion,
            [.. request.Hooks, "RunSettled"],
            []
        );
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);

        await Assert.ThrowsAsync<RemoteExtensionException>(() =>
            handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct).AsTask()
        );

        Assert.Equal(RemoteExtensionState.Refused, harness.Extension.Status.State);
        Assert.Contains("'RunSettled'", harness.Extension.Status.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_but_missing_hook_warns_and_is_dropped()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling", "context-building"]);
        harness.Child.Handshake = request => new InitializeResult(
            request.ProtocolVersion,
            ["ToolCalling"],
            []
        );
        var handler = (IToolCallingHandler)
            harness.Context.Handlers.Single(registered => registered is IToolCallingHandler);

        await handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct);

        Assert.Equal(RemoteExtensionState.Ready, harness.Extension.Status.State);
        Assert.Contains(
            harness.Context.Log.Warnings,
            warning => warning.Contains("declares hook 'ContextBuilding'", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            harness.Context.Handlers,
            registered => registered is IContextBuildingHandler
        );
    }

    [Fact]
    public async Task A_cancelled_hook_call_sends_a_cancel_request_to_the_child()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);
        var canceled = new TaskCompletionSource();
        var inFlight = new TaskCompletionSource();
        harness.Child.HookAnswers = async (_, _, ct) =>
        {
            inFlight.TrySetResult();
            using var registration = ct.Register(canceled.SetResult);
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        Task<ToolCallingResult> call = handler
            .HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), cts.Token)
            .AsTask();
        // Cancel only once the child is handling the call: a cancel that lands during the
        // handshake is a different path (and covered by the handshake tests).
        await inFlight.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        // The wire survived the cancel (a cancel that threw on the receiving side would have
        // closed it): a following hook call still round-trips and the child was not restarted.
        harness.Child.HookAnswers = (_, _, _) =>
            ValueTask.FromResult<JsonElement?>(
                JsonDocument.Parse("""{"outcome":"proceed"}""").RootElement.Clone()
            );
        await handler.HandleAsync(new ToolCallingPayload("r1", "c2", "read", EmptyArgs), Ct);
        Assert.Equal(1, harness.Child.StartCount);
    }

    [Fact]
    public async Task The_child_environment_is_base_plus_passthrough_plus_env()
    {
        Harness harness = Harness.Create(
            runtime: new ExtensionRuntime(
                "node",
                ["index.js"],
                Environment: new Dictionary<string, string> { ["DECLARED"] = "value" },
                EnvironmentPassthrough: ["PATH", "REMOTE_TESTS_ABSENT_VARIABLE"]
            ),
            hooks: ["tool-calling"]
        );
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);

        await handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct);

        IReadOnlyDictionary<string, string> environment = harness.Child.Requests[0].Environment;
        Assert.Equal("value", environment["DECLARED"]);
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), environment["PATH"]);
        Assert.DoesNotContain("REMOTE_TESTS_ABSENT_VARIABLE", environment.Keys);
    }

    [Fact]
    public async Task Relative_command_args_and_working_directory_resolve_against_the_directory()
    {
        Harness harness = Harness.Create(
            runtime: new ExtensionRuntime(
                "./bin/run",
                ["./index.js", "--flag"],
                WorkingDirectory: "sub"
            ),
            hooks: ["tool-calling"]
        );
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);

        await handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct);

        ProcessSessionRequest request = harness.Child.Requests[0];
        Assert.Equal(Path.Combine(harness.ExtensionDirectory, "bin", "run"), request.FileName);
        Assert.Equal(Path.Combine(harness.ExtensionDirectory, "index.js"), request.Arguments[0]);
        Assert.Equal("--flag", request.Arguments[1]);
        Assert.Equal(Path.Combine(harness.ExtensionDirectory, "sub"), request.WorkingDirectory);
    }

    [Fact]
    public async Task A_bare_command_stays_a_path_lookup()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);

        await handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct);

        Assert.Equal("node", harness.Child.Requests[0].FileName);
        Assert.Equal(harness.ExtensionDirectory, harness.Child.Requests[0].WorkingDirectory);
    }

    [Fact]
    public async Task A_crash_fails_in_flight_calls_immediately_with_one_notice_and_one_restart()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);
        var inFlight = new TaskCompletionSource();
        harness.Child.HookAnswers = async (_, _, ct) =>
        {
            inFlight.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        };
        Task<ToolCallingResult> call = handler
            .HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct)
            .AsTask();
        await inFlight.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        harness.Child.Crash();

        // No waiting for a timeout: the in-flight call fails immediately.
        await Assert.ThrowsAsync<RemoteExtensionException>(() =>
            call.WaitAsync(TimeSpan.FromSeconds(15), Ct)
        );
        Assert.Single(harness.Notices);
        await WaitAsync(() => harness.Child.StartCount == 2, "the one automatic restart");
        await WaitAsync(
            () => harness.Extension.Status.State == RemoteExtensionState.Ready,
            "the restarted child to be ready"
        );

        // A second crash exhausts the automatic budget: failed until a manual restart.
        harness.Child.HookAnswers = (_, _, _) =>
            ValueTask.FromResult<JsonElement?>(
                HookResultCodec.EncodeResult(
                    HookContracts.Find("ToolCalling")!,
                    new ToolCallingResult.Proceed(null)
                )
            );
        await handler.HandleAsync(new ToolCallingPayload("r1", "c2", "read", EmptyArgs), Ct);
        harness.Child.Crash();
        await WaitAsync(
            () => harness.Extension.Status.State == RemoteExtensionState.Failed,
            "the failed state after the second crash"
        );
        await Assert.ThrowsAsync<RemoteExtensionException>(() =>
            handler.HandleAsync(new ToolCallingPayload("r1", "c3", "read", EmptyArgs), Ct).AsTask()
        );
        Assert.Equal(2, harness.Child.StartCount);

        await harness.Extension.RestartAsync(Ct);

        Assert.Equal(3, harness.Child.StartCount);
        Assert.Equal(RemoteExtensionState.Ready, harness.Extension.Status.State);
    }

    [Fact]
    public async Task Session_end_sends_shutdown_and_kills_only_after_the_grace()
    {
        Harness patient = Harness.Create(hooks: ["tool-calling"]);
        patient.Child.ExitOnShutdown = true;
        var patientHandler = (IToolCallingHandler)Assert.Single(patient.Context.Handlers);
        await patientHandler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct);
        await patient.Extension.ShutdownAsync(Ct);
        Assert.False(patient.Child.Killed);
        Assert.Equal(RemoteExtensionState.Stopped, patient.Extension.Status.State);

        Harness stubborn = Harness.Create(hooks: ["tool-calling"]);
        stubborn.Child.ExitOnShutdown = false;
        var stubbornHandler = (IToolCallingHandler)Assert.Single(stubborn.Context.Handlers);
        await stubbornHandler.HandleAsync(
            new ToolCallingPayload("r1", "c1", "read", EmptyArgs),
            Ct
        );
        await stubborn.Extension.ShutdownAsync(Ct);
        Assert.True(stubborn.Child.Killed);
    }

    [Fact]
    public async Task Background_mode_starts_only_when_triggered()
    {
        Harness harness = Harness.Create(
            runtime: new ExtensionRuntime("node", ["index.js"], Startup: "background"),
            hooks: ["tool-calling"]
        );

        Assert.True(harness.Extension.Status.State == RemoteExtensionState.NotStarted);
        Assert.Equal(0, harness.Child.StartCount);

        await harness.Extension.StartInBackgroundAsync(Ct);

        Assert.Equal(1, harness.Child.StartCount);
        Assert.Equal(RemoteExtensionState.Ready, harness.Extension.Status.State);
    }

    [Fact]
    public async Task Host_to_child_service_calls_cross_the_wire()
    {
        Harness harness = Harness.Create(
            runtime: new ExtensionRuntime("node", ["index.js"]),
            hooks: [],
            services: ["memory"]
        );
        harness.Child.ServiceAnswers = envelope =>
        {
            Assert.Equal("memory", envelope.Service);
            Assert.Equal("store", envelope.Method);
            return JsonSerializer.SerializeToElement(new { stored = 1 }, ScriptedChild.Options);
        };

        JsonElement? result = await harness.Extension.InvokeServiceAsync(
            "memory",
            "store",
            JsonDocument.Parse("""{"text":"x"}""").RootElement.Clone(),
            Ct
        );

        Assert.Equal(1, result!.Value.GetProperty("stored").GetInt32());
        Assert.IsAssignableFrom<IJsonService>(harness.Context.Services["memory"]);
    }

    [Fact]
    public async Task Child_to_host_service_calls_cross_the_wire()
    {
        Harness harness = Harness.Create(
            runtime: new ExtensionRuntime("node", ["index.js"]),
            hooks: ["tool-calling"],
            services: []
        );
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);
        await handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct);

        JsonElement? workspace = await harness.Child.ConsumeServiceAsync(
            "core/workspace",
            "get",
            null,
            Ct
        );

        Assert.Equal("/work", workspace!.Value.GetProperty("worktreeRoot").GetString());
    }

    [Fact]
    public async Task A_hook_handler_calls_back_into_the_host_while_the_hook_is_in_flight()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"]);
        var hostService = new HostEchoService();
        harness.Context.Services["echo"] = hostService;
        harness.Child.HookAnswers = async (_, _, ct) =>
        {
            // The reentrant case: a host service is consumed from inside a hook handler.
            JsonElement? answer = await harness.Child.ConsumeServiceAsync(
                "echo",
                "find",
                JsonDocument.Parse("""{"query":"x"}""").RootElement.Clone(),
                ct
            );
            Assert.Equal("found", answer!.Value.GetProperty("state").GetString());
            return HookResultCodec.EncodeResult(
                HookContracts.Find("ToolCalling")!,
                new ToolCallingResult.Proceed(null)
            );
        };
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);

        ToolCallingResult result = await handler.HandleAsync(
            new ToolCallingPayload("r1", "c1", "read", EmptyArgs),
            Ct
        );

        Assert.IsType<ToolCallingResult.Proceed>(result);
        Assert.Equal(1, hostService.Calls);
    }

    [Fact]
    public async Task An_unregistered_host_service_is_a_teaching_error()
    {
        Harness harness = Harness.Create(hooks: ["tool-calling"], services: []);
        var handler = (IToolCallingHandler)Assert.Single(harness.Context.Handlers);
        await handler.HandleAsync(new ToolCallingPayload("r1", "c1", "read", EmptyArgs), Ct);

        RemoteCallException exception = await Assert.ThrowsAsync<RemoteCallException>(() =>
            harness.Child.ConsumeServiceAsync("absent", "x", null, Ct)
        );

        Assert.Equal(-32601, exception.Code);
        Assert.Contains("not registered", exception.PeerMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_gating_hook_that_does_not_answer_in_time_is_blocked_fail_closed()
    {
        Harness harness = Harness.Create(
            hooks: ["tool-calling", "project-trust"],
            runnerOptions: new Lunate.Extensibility.HookRunnerOptions
            {
                HandlerTimeout = TimeSpan.FromMilliseconds(200),
            }
        );
        harness.Child.HookAnswers = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        };
        var runner = new Lunate.Extensibility.HookRunner(
            new Lunate.Extensibility.HookRunnerOptions
            {
                HandlerTimeout = TimeSpan.FromMilliseconds(200),
            },
            harness.Context.Log
        );
        foreach (IHookHandler handler in harness.Context.Handlers)
        {
            runner.Register(harness.Context.Id, handler);
        }

        ToolCallingResult blocked = await runner.RunToolCallingAsync(
            new ToolCallingPayload("r1", "c1", "read", EmptyArgs),
            Ct
        );

        Assert.IsType<ToolCallingResult.Block>(blocked);
        Assert.Contains(
            harness.Context.Log.Errors,
            entry => entry.Contains("ToolCalling", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_project_trust_hook_that_does_not_answer_in_time_denies()
    {
        Harness harness = Harness.Create(hooks: ["project-trust"]);
        harness.Child.HookAnswers = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        };
        var runner = new Lunate.Extensibility.HookRunner(
            new Lunate.Extensibility.HookRunnerOptions
            {
                HandlerTimeout = TimeSpan.FromMilliseconds(200),
            },
            harness.Context.Log
        );
        runner.Register(harness.Context.Id, harness.Context.Handlers.Single());

        Lunate.Extensibility.ProjectTrustDispatch dispatch = await runner.RunProjectTrustAsync(
            new ProjectTrustPayload("other", "/ext", "repo", "/work"),
            cancellationToken: Ct
        );

        Assert.IsType<ProjectTrustResult.Deny>(dispatch.Result);
    }

    [Fact]
    public async Task An_observer_failure_after_a_crash_is_dropped_and_reported()
    {
        Harness harness = Harness.Create(hooks: ["session-started"]);
        var runner = new Lunate.Extensibility.HookRunner(null, harness.Context.Log);
        runner.Register(harness.Context.Id, harness.Context.Handlers.Single());
        harness.Child.ExitOnShutdown = true;
        await harness.Extension.StartInBackgroundAsync(Ct);
        // The first crash spends the automatic restart; the second reads failed.
        harness.Child.Crash();
        await WaitAsync(() => harness.Child.StartCount == 2, "the automatic restart");
        await WaitAsync(
            () => harness.Extension.Status.State == RemoteExtensionState.Ready,
            "the restarted child"
        );
        harness.Child.Crash();
        await WaitAsync(
            () => harness.Extension.Status.State == RemoteExtensionState.Failed,
            "the failed state"
        );

        await runner.RunSessionStartedAsync(new SessionStartedPayload("/work", "repo"), Ct);

        Assert.Contains(
            harness.Context.Log.Errors,
            entry => entry.Contains("SessionStarted", StringComparison.Ordinal)
        );
    }

    private static async Task WaitAsync(Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 750; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20, Ct);
        }

        Assert.Fail($"timed out waiting for {what}");
    }

    private sealed class HostEchoService : IBackgroundService, IJsonService
    {
        public int Calls { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<JsonElement?> InvokeAsync(
            string method,
            JsonElement? arguments,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return ValueTask.FromResult<JsonElement?>(
                JsonSerializer.SerializeToElement(new { state = "found" }, ScriptedChild.Options)
            );
        }
    }

    private sealed class Harness
    {
        private Harness(
            RemoteExtension extension,
            ScriptedChild child,
            FakeExtensionContext context,
            string directory,
            List<string> notices
        )
        {
            Extension = extension;
            Child = child;
            Context = context;
            ExtensionDirectory = directory;
            Notices = notices;
        }

        public RemoteExtension Extension { get; }

        public ScriptedChild Child { get; }

        public FakeExtensionContext Context { get; }

        public string ExtensionDirectory { get; }

        public List<string> Notices { get; }

        public static Harness Create(
            ExtensionRuntime? runtime = null,
            string[]? hooks = null,
            string[]? services = null,
            Lunate.Extensibility.HookRunnerOptions? runnerOptions = null
        )
        {
            runtime ??= new ExtensionRuntime("node", ["index.js"]);
            var context = new FakeExtensionContext();
            var child = new ScriptedChild();
            var notices = new List<string>();
            var directory = Path.Combine(
                Path.GetTempPath(),
                "lunate-remote-tests",
                Guid.NewGuid().ToString("N")
            );
            System.IO.Directory.CreateDirectory(directory);
            var extension = new RemoteExtension(
                new RemoteExtensionOptions
                {
                    Manifest = new ExtensionManifest
                    {
                        Id = context.Id,
                        Version = "0.1.0",
                        ApiVersion = "^1.0.0",
                        Runtime = runtime,
                        Hooks = hooks ?? [],
                        Services = services ?? [],
                    },
                    Directory = directory,
                    Workspace = Workspace,
                    ProcessRunner = child.Runner,
                    Context = context,
                    Notify = notices.Add,
                    UnregisterHook = handler => context.Handlers.Remove(handler),
                    ShutdownGrace = TimeSpan.FromMilliseconds(300),
                }
            );
            return new Harness(extension, child, context, directory, notices);
        }
    }
}
