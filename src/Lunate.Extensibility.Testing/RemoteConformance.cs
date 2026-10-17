using System.Text.Json;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Testing;

/// <summary>One wire-conformance sample: a payload DTO and its catalogue row.</summary>
public sealed record HookPayloadSample(string Name, HookContract Contract, object Payload);

/// <summary>One wire-conformance sample: a result DTO (or union variant) and its catalogue row.</summary>
public sealed record HookResultSample(string Name, HookContract Contract, object Result);

/// <summary>
/// The remote wire conformance inventory: one sample per hook payload DTO and per result DTO, and
/// the reflection check that proves the inventory is exhaustive — every type marked
/// <see cref="IHookPayload"/>/<see cref="IHookResult"/> must have a sample, so a new DTO cannot
/// ship without a wire round-trip entry. <see cref="RemoteConformanceRunner"/> drives the samples
/// (and an arbitrary extension executable) over the real wire.
/// </summary>
public static class RemoteConformance
{
    public static IReadOnlyList<HookPayloadSample> Payloads { get; } = BuildPayloads();

    public static IReadOnlyList<HookResultSample> Results { get; } = BuildResults();

    /// <summary>The payload or result types the inventory does not cover; empty when complete.</summary>
    public static IReadOnlyList<string> MissingCoverage()
    {
        HashSet<Type> payloads = [.. Payloads.Select(sample => sample.Payload.GetType())];
        HashSet<Type> results = [.. Results.Select(sample => sample.Result.GetType())];
        List<string> missing = [];

        foreach (Type type in MarkedTypes(typeof(IHookPayload)))
        {
            if (!payloads.Contains(type))
            {
                missing.Add(type.FullName ?? type.Name);
            }
        }

        foreach (Type type in MarkedTypes(typeof(IHookResult)))
        {
            if (!results.Contains(type))
            {
                missing.Add(type.FullName ?? type.Name);
            }
        }

        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    /// <summary>Canonical JSON text: parsed and re-serialized, so whitespace never matters.</summary>
    public static string Canonical(JsonElement element) => JsonNodeNormalize(element);

    private static IReadOnlyList<Type> MarkedTypes(Type marker) =>
        [
            .. typeof(IHookPayload)
                .Assembly.GetTypes()
                .Where(type =>
                    type is { IsAbstract: false, IsInterface: false }
                    && marker.IsAssignableFrom(type)
                    && !type.IsDefined(
                        typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute),
                        inherit: false
                    )
                ),
        ];

    private static HookContract Contract(string name) =>
        HookContracts.Find(name)
        ?? throw new InvalidOperationException($"no catalogue row for hook '{name}'");

    private static List<HookPayloadSample> BuildPayloads()
    {
        JsonElement arguments = JsonDocument.Parse("""{"path":"a.txt"}""").RootElement.Clone();
        return
        [
            new(
                "ProjectTrustPayload",
                Contract("ProjectTrust"),
                new ProjectTrustPayload("ext", "/ext", "repo", "/work")
            ),
            new(
                "SessionStartedPayload",
                Contract("SessionStarted"),
                new SessionStartedPayload("/work", "repo")
            ),
            new(
                "SessionEndingPayload",
                Contract("SessionEnding"),
                new SessionEndingPayload("/work", "repo")
            ),
            new(
                "InputReceivedPayload",
                Contract("InputReceived"),
                new InputReceivedPayload("hello")
            ),
            new(
                "RunStartingPayload",
                Contract("RunStarting"),
                new RunStartingPayload(
                    "r1",
                    [new PromptSection("system", "base") { Source = "ext" }],
                    ["read", "write"]
                )
                {
                    ParentRunId = "p1",
                }
            ),
            new(
                "ContextBuildingPayload",
                Contract("ContextBuilding"),
                new ContextBuildingPayload(
                    "r1",
                    [new ContextMessage("system", "base") { Source = "ext" }]
                )
            ),
            new(
                "ProviderStreamEventPayload",
                Contract("ProviderStreamEvent"),
                new ProviderStreamEventPayload("r1", "model-1", "text", "stop")
            ),
            new(
                "MessageCompletedPayload",
                Contract("MessageCompleted"),
                new MessageCompletedPayload("r1", "assistant", "text")
            ),
            new(
                "ToolCallingPayload",
                Contract("ToolCalling"),
                new ToolCallingPayload("r1", "c1", "read", arguments)
                {
                    Annotations = ["read-only"],
                }
            ),
            new(
                "ToolResultReadyPayload",
                Contract("ToolResultReady"),
                new ToolResultReadyPayload("r1", "c1", "read", arguments, "output", false)
            ),
            new("TurnEndedPayload", Contract("TurnEnded"), new TurnEndedPayload("r1")),
            new("RunSettledPayload", Contract("RunSettled"), new RunSettledPayload("r1")),
            new(
                "CompactingPayload",
                Contract("Compacting"),
                new CompactingPayload("r1", [new ContextMessage("user", "old")])
            ),
            new(
                "ModelChangedPayload",
                Contract("ModelChanged"),
                new ModelChangedPayload("r1", "model-2")
            ),
            new(
                "ToolsChangedPayload",
                Contract("ToolsChanged"),
                new ToolsChangedPayload("r1", ["read", "write"])
            ),
            new(
                "FileChangedPayload",
                Contract("FileChanged"),
                new FileChangedPayload("/work/a.txt", "a.txt", "/work", "r1", "modified")
            ),
        ];
    }

    private static List<HookResultSample> BuildResults()
    {
        JsonElement arguments = JsonDocument.Parse("""{"path":"a.txt"}""").RootElement.Clone();
        JsonElement data = JsonDocument.Parse("""{"diagnostics":[]}""").RootElement.Clone();
        JsonElement entry = JsonDocument.Parse("""{"note":"remember"}""").RootElement.Clone();
        return
        [
            new(
                "ProjectTrustResult.Allow",
                Contract("ProjectTrust"),
                new ProjectTrustResult.Allow()
            ),
            new(
                "ProjectTrustResult.Deny",
                Contract("ProjectTrust"),
                new ProjectTrustResult.Deny("no")
            ),
            new(
                "InputReceivedResult.PassThrough",
                Contract("InputReceived"),
                new InputReceivedResult.PassThrough()
            ),
            new(
                "InputReceivedResult.Transform",
                Contract("InputReceived"),
                new InputReceivedResult.Transform("changed")
            ),
            new(
                "InputReceivedResult.Consume",
                Contract("InputReceived"),
                new InputReceivedResult.Consume()
            ),
            new("RunStartingResult.None", Contract("RunStarting"), new RunStartingResult.None()),
            new(
                "RunStartingResult.Apply",
                Contract("RunStarting"),
                new RunStartingResult.Apply(
                    [new PromptSectionEdit("memory", "remember") { Source = "ext" }],
                    ["read"]
                )
            ),
            new(
                "ContextBuildingResult",
                Contract("ContextBuilding"),
                new ContextBuildingResult([
                    new ContextMessage("user", "injected") { Source = "ext" },
                ])
            ),
            new(
                "MessageCompletedResult.Keep",
                Contract("MessageCompleted"),
                new MessageCompletedResult.Keep()
            ),
            new(
                "MessageCompletedResult.Replace",
                Contract("MessageCompleted"),
                new MessageCompletedResult.Replace("fixed")
            ),
            new(
                "ToolCallingResult.Proceed",
                Contract("ToolCalling"),
                new ToolCallingResult.Proceed(null)
            ),
            new(
                "ToolCallingResult.Proceed.Args",
                Contract("ToolCalling"),
                new ToolCallingResult.Proceed(arguments)
            ),
            new(
                "ToolCallingResult.Block",
                Contract("ToolCalling"),
                new ToolCallingResult.Block("not allowed")
            ),
            new(
                "ToolResultReadyResult",
                Contract("ToolResultReady"),
                new ToolResultReadyResult("output", null)
            ),
            new(
                "ToolResultReadyResult.Data",
                Contract("ToolResultReady"),
                new ToolResultReadyResult("output", data)
            ),
            new(
                "ToolResultReadyResult.Structured",
                Contract("ToolResultReady"),
                new ToolResultReadyResult("output", data, data)
            ),
            new("TurnEndedResult.None", Contract("TurnEnded"), new TurnEndedResult.None()),
            new(
                "TurnEndedResult.TurnEnded",
                Contract("TurnEnded"),
                new TurnEndedResult.TurnEnded([new TurnEndedEntry("memory", entry)], true)
            ),
            new(
                "CompactingResult.UseDefault",
                Contract("Compacting"),
                new CompactingResult.UseDefault()
            ),
            new(
                "CompactingResult.Provide",
                Contract("Compacting"),
                new CompactingResult.Provide("summary")
            ),
        ];
    }

    private static string JsonNodeNormalize(JsonElement element) =>
        System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText())!.ToJsonString();
}
