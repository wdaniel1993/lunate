using System.Globalization;
using System.Text.Json;
using Lunate.Coding;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Testing;

/// <summary>What to probe the extension with; the defaults suit the node reference extension.</summary>
public sealed class RemoteConformanceOptions
{
    /// <summary>The executable to start; a relative path resolves against the working directory.</summary>
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>The child's working directory; also the workspace the runner declares.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The child's environment; null uses the shared scrubber (the base set only), which is what
    /// the host gives real extensions.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>The services the handshake declares; the child must not implement others.</summary>
    public IReadOnlyList<string> Services { get; init; } = ["echo"];

    /// <summary>The hook probed over the wire (an interceptor); the default is ToolCalling.</summary>
    public string Hook { get; init; } = "ToolCalling";

    /// <summary>The host → child probe: the registered service and method to call.</summary>
    public string ProbeService { get; init; } = "echo";

    public string ProbeMethod { get; init; } = "repeat";

    public JsonElement? ProbeArguments { get; init; }

    /// <summary>The step timeout (start, handshake, each round trip).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public IExtensionLog? Log { get; init; }

    /// <summary>The core process capability; null uses the production <c>core/process</c> runner.</summary>
    public IProcessRunner? ProcessRunner { get; init; }
}

/// <summary>One conformance step and its outcome.</summary>
public sealed record RemoteConformanceStep(string Name, bool Passed, string Detail);

/// <summary>The runner's report: every step in order; <see cref="Succeeded"/> is all green.</summary>
public sealed record RemoteConformanceReport(IReadOnlyList<RemoteConformanceStep> Steps)
{
    public bool Succeeded => Steps.All(step => step.Passed);

    public string Summary =>
        string.Join(
            "; ",
            Steps.Select(step => $"{step.Name}: {(step.Passed ? "ok" : "FAILED — " + step.Detail)}")
        );
}

/// <summary>
/// The wire conformance runner: point it at any remote extension executable and it starts the
/// process, performs the <c>initialize</c> handshake, round-trips the probe hook over the real
/// wire, probes a service call in each direction (host → child through <c>service-call</c>, and
/// child → host by observing the child's own calls while the hook is in flight), sends
/// <c>shutdown</c> and reports each step. The extension surface (hooks and services) must be
/// compatible with the options; the defaults suit the node reference extension.
/// </summary>
public static class RemoteConformanceRunner
{
    public static async Task<RemoteConformanceReport> RunAsync(
        RemoteConformanceOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);

        var steps = new List<RemoteConformanceStep>();
        string workingDirectory = Path.GetFullPath(options.WorkingDirectory);
        IProcessRunner runner = options.ProcessRunner ?? new ProcessRunner();
        WorkspaceInfo workspace = new(workingDirectory, null, null);
        HookContract? hook = HookContracts.Find(options.Hook);
        var childServiceCalls = new List<string>();

        RemoteProcessChannel? channel = null;
        RemoteWire? wire = null;
        try
        {
            channel = await RemoteProcessChannel
                .StartAsync(
                    runner,
                    new ProcessSessionRequest(
                        options.FileName,
                        options.Arguments,
                        workingDirectory,
                        options.Environment ?? EnvironmentScrubber.Build(),
                        MaxLineBytes: 16 * 1024 * 1024
                    ),
                    "conformance",
                    options.Log ?? NullLog.Instance,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
            steps.Add(new RemoteConformanceStep("start", true, "the process started"));

            wire = new RemoteWire(
                channel,
                new RemoteWireOptions { DefaultCallTimeout = options.Timeout, Log = options.Log }
            );
            wire.RequestReceived += (method, parameters, ct) =>
            {
                if (method.StartsWith("service/", StringComparison.Ordinal))
                {
                    childServiceCalls.Add(method);
                    if (method == RemoteProtocol.ServiceMethod("core/workspace", "get"))
                    {
                        return ValueTask.FromResult<JsonElement?>(
                            JsonSerializer.SerializeToElement(
                                workspace,
                                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                            )
                        );
                    }

                    return ValueTask.FromResult<JsonElement?>(null);
                }

                throw new RemoteCallException(-32601, $"the runner does not serve '{method}'");
            };
            wire.Start();

            // The handshake declares the whole hook catalogue (declared-but-missing only warns)
            // and the option services; anything else the child implements refuses it.
            var initialize = new InitializeRequest(
                RemoteProtocol.Version,
                RemoteProtocol.Capabilities,
                JsonDocument.Parse("{}").RootElement.Clone(),
                workspace,
                [.. HookContracts.All.Select(contract => contract.Name)],
                options.Services
            );
            JsonElement? handshake = await wire.CallAsync(
                    RemoteProtocol.InitializeMethod,
                    JsonSerializer.SerializeToElement(
                        initialize,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    ),
                    options.Timeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            InitializeResult? answer = handshake?.Deserialize<InitializeResult>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );
            if (answer is null || answer.ProtocolVersion != RemoteProtocol.Version)
            {
                steps.Add(
                    new RemoteConformanceStep(
                        "handshake",
                        false,
                        $"the extension answered protocol {(answer is null ? "nothing" : answer.ProtocolVersion.ToString(CultureInfo.InvariantCulture))}; expected {RemoteProtocol.Version.ToString(CultureInfo.InvariantCulture)}"
                    )
                );
                return new RemoteConformanceReport(steps);
            }

            steps.Add(
                new RemoteConformanceStep(
                    "handshake",
                    true,
                    $"protocol {RemoteProtocol.Version.ToString(CultureInfo.InvariantCulture)}; hooks: [{string.Join(", ", answer.Hooks)}]; services: [{string.Join(", ", answer.Services)}]"
                )
            );

            if (hook is null)
            {
                steps.Add(
                    new RemoteConformanceStep(
                        "hook round trip",
                        false,
                        $"unknown hook '{options.Hook}'"
                    )
                );
                return new RemoteConformanceReport(steps);
            }

            HookCallEnvelope envelope = HookProbeEnvelope(hook, workspace);
            JsonElement? hookResult = await wire.CallAsync(
                    hook.Method,
                    JsonSerializer.SerializeToElement(
                        envelope,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    ),
                    options.Timeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (hookResult is null)
            {
                steps.Add(
                    new RemoteConformanceStep("hook round trip", false, "the hook answered nothing")
                );
                return new RemoteConformanceReport(steps);
            }

            try
            {
                object decoded = HookResultCodec.DecodeResult(hook, hookResult.Value);
                steps.Add(
                    new RemoteConformanceStep(
                        "hook round trip",
                        true,
                        $"answered {decoded.GetType().Name}"
                    )
                );
            }
            catch (Exception exception)
            {
                steps.Add(new RemoteConformanceStep("hook round trip", false, exception.Message));
                return new RemoteConformanceReport(steps);
            }

            steps.Add(
                new RemoteConformanceStep(
                    "service call child -> host",
                    childServiceCalls.Count > 0,
                    childServiceCalls.Count > 0
                        ? $"observed {string.Join(", ", childServiceCalls)}"
                        : "the child made no host service call while the hook was in flight"
                )
            );

            JsonElement? serviceResult = await wire.CallAsync(
                    RemoteProtocol.ServiceCallMethod,
                    JsonSerializer.SerializeToElement(
                        new ServiceCallEnvelope(
                            options.ProbeService,
                            options.ProbeMethod,
                            options.ProbeArguments
                        ),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    ),
                    options.Timeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            steps.Add(
                new RemoteConformanceStep(
                    "service call host -> child",
                    serviceResult is not null,
                    serviceResult is null
                        ? "the service-call answered nothing"
                        : $"answered {serviceResult.Value.GetRawText()}"
                )
            );

            try
            {
                await wire.CallAsync(
                        RemoteProtocol.ShutdownMethod,
                        null,
                        options.Timeout,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A child that exits on shutdown may never answer; the process state decides.
            }

            using var grace = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            grace.CancelAfter(options.Timeout);
            bool exited;
            try
            {
                await channel.Session.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                exited = true;
            }
            catch (Exception)
            {
                exited = channel.Session.HasExited;
            }

            if (!exited)
            {
                await channel.Session.KillAsync().ConfigureAwait(false);
            }

            steps.Add(
                new RemoteConformanceStep(
                    "shutdown",
                    exited,
                    exited ? "the process exited" : "the process had to be killed"
                )
            );
            return new RemoteConformanceReport(steps);
        }
        catch (Exception exception)
        {
            steps.Add(
                new RemoteConformanceStep(
                    "transport",
                    false,
                    $"{exception.GetType().Name}: {exception.Message}"
                )
            );
            return new RemoteConformanceReport(steps);
        }
        finally
        {
            if (wire is not null)
            {
                await wire.DisposeAsync().ConfigureAwait(false);
            }

            if (channel is not null)
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static HookCallEnvelope HookProbeEnvelope(HookContract hook, WorkspaceInfo workspace)
    {
        object payload = hook.Name switch
        {
            "ToolCalling" => new ToolCallingPayload(
                "conformance-run",
                "conformance-call",
                "read",
                JsonDocument.Parse("""{"path":"conformance.txt"}""").RootElement.Clone()
            ),
            "ProjectTrust" => new ProjectTrustPayload(
                "conformance",
                workspace.WorktreeRoot,
                "conformance",
                workspace.WorktreeRoot
            ),
            "InputReceived" => new InputReceivedPayload("conformance"),
            "RunStarting" => new RunStartingPayload("conformance-run", [], []),
            "ContextBuilding" => new ContextBuildingPayload("conformance-run", []),
            "MessageCompleted" => new MessageCompletedPayload(
                "conformance-run",
                "assistant",
                "conformance"
            ),
            "ToolResultReady" => new ToolResultReadyPayload(
                "conformance-run",
                "conformance-call",
                "read",
                JsonDocument.Parse("{}").RootElement.Clone(),
                "conformance",
                false
            ),
            "TurnEnded" => new TurnEndedPayload("conformance-run"),
            "Compacting" => new CompactingPayload("conformance-run", []),
            _ => throw new ArgumentException($"hook '{hook.Name}' has no probe payload."),
        };
        return new HookCallEnvelope(
            RemoteCallContext.ForPayload(payload, workspace),
            HookResultCodec.EncodePayload(hook, payload)
        );
    }

    private sealed class NullLog : IExtensionLog
    {
        public static NullLog Instance { get; } = new();

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message) { }
    }
}
