using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>How the host forwards one hook: observers as notifications, interceptors as requests.</summary>
public enum HookKind
{
    Observer,
    Interceptor,
}

/// <summary>
/// One row of the hook wire catalogue: the hook name on the wire (<c>hook/&lt;Name&gt;</c>), its
/// forwarding kind, its payload DTO, its result family (null for observers) and the codec that
/// encodes/decodes the result. <see cref="ResultTypes"/> lists every result DTO the row covers,
/// so the conformance suite can prove the catalogue has no gaps.
/// </summary>
public sealed record HookContract(
    string Name,
    HookKind Kind,
    Type PayloadType,
    Type? ResultType,
    IReadOnlyList<Type> ResultTypes,
    Func<object, JsonElement>? EncodeResult = null,
    Func<JsonElement, object>? DecodeResult = null
)
{
    /// <summary>The JSON-RPC method this hook is forwarded under.</summary>
    public string Method => "hook/" + Name;
}

/// <summary>
/// The v1 hook wire catalogue: the same hooks the in-process contract assembly defines, with the
/// pinned forwarding kinds (observers fire-and-forget, interceptors request/response).
/// </summary>
public static class HookContracts
{
    public static IReadOnlyList<HookContract> All { get; } =
    [
        Interceptor<ProjectTrustPayload, ProjectTrustResult>(
            "ProjectTrust",
            HookResultCodec.ProjectTrustResultTypes,
            HookResultCodec.EncodeProjectTrust,
            HookResultCodec.DecodeProjectTrust
        ),
        Observer<SessionStartedPayload>("SessionStarted"),
        Observer<SessionEndingPayload>("SessionEnding"),
        Interceptor<InputReceivedPayload, InputReceivedResult>(
            "InputReceived",
            HookResultCodec.InputReceivedResultTypes,
            HookResultCodec.EncodeInputReceived,
            HookResultCodec.DecodeInputReceived
        ),
        Interceptor<RunStartingPayload, RunStartingResult>(
            "RunStarting",
            HookResultCodec.RunStartingResultTypes,
            HookResultCodec.EncodeRunStarting,
            HookResultCodec.DecodeRunStarting
        ),
        Interceptor<ContextBuildingPayload, ContextBuildingResult>(
            "ContextBuilding",
            HookResultCodec.ContextBuildingResultTypes,
            HookResultCodec.EncodeContextBuilding,
            HookResultCodec.DecodeContextBuilding
        ),
        Observer<ProviderStreamEventPayload>("ProviderStreamEvent"),
        Interceptor<MessageCompletedPayload, MessageCompletedResult>(
            "MessageCompleted",
            HookResultCodec.MessageCompletedResultTypes,
            HookResultCodec.EncodeMessageCompleted,
            HookResultCodec.DecodeMessageCompleted
        ),
        Interceptor<ToolCallingPayload, ToolCallingResult>(
            "ToolCalling",
            HookResultCodec.ToolCallingResultTypes,
            HookResultCodec.EncodeToolCalling,
            HookResultCodec.DecodeToolCalling
        ),
        Interceptor<ToolResultReadyPayload, ToolResultReadyResult>(
            "ToolResultReady",
            HookResultCodec.ToolResultReadyResultTypes,
            HookResultCodec.EncodeToolResultReady,
            HookResultCodec.DecodeToolResultReady
        ),
        Interceptor<TurnEndedPayload, TurnEndedResult>(
            "TurnEnded",
            HookResultCodec.TurnEndedResultTypes,
            HookResultCodec.EncodeTurnEnded,
            HookResultCodec.DecodeTurnEnded
        ),
        Observer<RunSettledPayload>("RunSettled"),
        Interceptor<CompactingPayload, CompactingResult>(
            "Compacting",
            HookResultCodec.CompactingResultTypes,
            HookResultCodec.EncodeCompacting,
            HookResultCodec.DecodeCompacting
        ),
        Observer<ModelChangedPayload>("ModelChanged"),
        Observer<ToolsChangedPayload>("ToolsChanged"),
        Observer<FileChangedPayload>("FileChanged"),
    ];

    /// <summary>The catalogue row for a payload DTO, or null when the DTO is not a hook payload.</summary>
    public static HookContract? FindByPayloadType(Type payloadType) =>
        All.FirstOrDefault(contract => contract.PayloadType == payloadType);

    /// <summary>The catalogue row for a wire name, or null.</summary>
    public static HookContract? Find(string name) =>
        All.FirstOrDefault(contract =>
            string.Equals(contract.Name, name, StringComparison.Ordinal)
        );

    private static HookContract Observer<TPayload>(string name)
        where TPayload : IHookPayload => new(name, HookKind.Observer, typeof(TPayload), null, []);

    private static HookContract Interceptor<TPayload, TResult>(
        string name,
        IReadOnlyList<Type> resultTypes,
        Func<TResult, JsonElement> encode,
        Func<JsonElement, TResult> decode
    )
        where TPayload : IHookPayload
        where TResult : IHookResult =>
        new(
            name,
            HookKind.Interceptor,
            typeof(TPayload),
            typeof(TResult),
            resultTypes,
            result => encode((TResult)result),
            element => decode(element)
        );
}

/// <summary>
/// The run identity every forwarded call carries: the run's id (null outside a run), the parent
/// run's id when this is a child (subagent) run, and the workspace.
/// </summary>
public sealed record RemoteCallContext(string? RunId, string? ParentRunId, WorkspaceInfo Workspace)
{
    /// <summary>Builds the context from a payload; payloads without a run carry null ids.</summary>
    public static RemoteCallContext ForPayload(object payload, WorkspaceInfo workspace) =>
        new(RunIdOf(payload), (payload as IParentRunIdPayload)?.ParentRunId, workspace);

    private static string? RunIdOf(object payload) =>
        payload switch
        {
            RunStartingPayload value => value.RunId,
            ContextBuildingPayload value => value.RunId,
            ProviderStreamEventPayload value => value.RunId,
            MessageCompletedPayload value => value.RunId,
            ToolCallingPayload value => value.RunId,
            ToolResultReadyPayload value => value.RunId,
            TurnEndedPayload value => value.RunId,
            RunSettledPayload value => value.RunId,
            CompactingPayload value => value.RunId,
            ModelChangedPayload value => value.RunId,
            ToolsChangedPayload value => value.RunId,
            FileChangedPayload value => value.RunId,
            _ => null,
        };
}

/// <summary>One forwarded hook call: the run context plus the payload object.</summary>
public sealed record HookCallEnvelope(RemoteCallContext Call, JsonElement Payload);

/// <summary>One host → child service call: the registered service, the method and the arguments.</summary>
public sealed record ServiceCallEnvelope(string Service, string Method, JsonElement? Arguments);
