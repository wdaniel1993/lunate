using System.Text.Json;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;
using Lunate.Extensibility.Testing;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// The wire conformance suite: the inventory is exhaustive (a new hook DTO without an entry fails
/// by name), and every hook payload and result DTO and the service-call envelope round-trips over
/// the actual wire — serialized, framed, parsed, decoded and re-encoded by the peer.
/// </summary>
public sealed class RemoteConformanceTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly WorkspaceInfo Workspace = new("/work", "/repo", "/repo/.git");

    [Fact]
    public async Task A_child_that_never_calls_back_still_passes_the_mandatory_steps()
    {
        var child = new ScriptedChild();
        child.HookAnswers = (_, _, _) =>
            ValueTask.FromResult<JsonElement?>(
                JsonSerializer.SerializeToElement(new { outcome = "proceed" })
            );

        RemoteConformanceReport report = await RemoteConformanceRunner.RunAsync(
            new RemoteConformanceOptions
            {
                FileName = "node",
                WorkingDirectory = Path.GetTempPath(),
                ProcessRunner = child.Runner,
                Timeout = TimeSpan.FromSeconds(15),
            },
            Ct
        );

        RemoteConformanceStep childToHost = Assert.Single(
            report.Steps,
            step => step.Name == "service call child -> host"
        );
        Assert.False(childToHost.Passed);
        Assert.False(childToHost.Required);
        Assert.True(report.Succeeded, report.Summary);
    }

    [Fact]
    public async Task The_run_invokes_a_command_when_the_child_advertises_the_capability()
    {
        var child = new ScriptedChild();

        RemoteConformanceReport report = await RemoteConformanceRunner.RunAsync(
            new RemoteConformanceOptions
            {
                FileName = "node",
                WorkingDirectory = Path.GetTempPath(),
                ProcessRunner = child.Runner,
                Timeout = TimeSpan.FromSeconds(15),
            },
            Ct
        );

        Assert.True(report.Succeeded, report.Summary);
        RemoteConformanceStep listing = Assert.Single(
            report.Steps,
            step => step.Name == "command listing"
        );
        Assert.Contains(
            "advertises the commands capability",
            listing.Detail,
            StringComparison.Ordinal
        );
        RemoteConformanceStep invoke = Assert.Single(
            report.Steps,
            step => step.Name == "command invoke"
        );
        Assert.True(invoke.Passed, invoke.Detail);
        Assert.Contains("pong conformance", invoke.Detail, StringComparison.Ordinal);
        CommandInvokeRequest request = Assert.Single(child.CommandRequests);
        Assert.Equal("ping", request.Name);
        Assert.Equal("conformance", request.Argument);
    }

    [Fact]
    public async Task A_child_without_the_capability_is_reported_as_declared_not_provided()
    {
        var child = new ScriptedChild { AdvertisesCommands = false };

        RemoteConformanceReport report = await RemoteConformanceRunner.RunAsync(
            new RemoteConformanceOptions
            {
                FileName = "node",
                WorkingDirectory = Path.GetTempPath(),
                ProcessRunner = child.Runner,
                Timeout = TimeSpan.FromSeconds(15),
            },
            Ct
        );

        Assert.True(report.Succeeded, report.Summary);
        RemoteConformanceStep listing = Assert.Single(
            report.Steps,
            step => step.Name == "command listing"
        );
        Assert.True(listing.Passed);
        Assert.Contains("declared, not provided", listing.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Steps, step => step.Name == "command invoke");
    }

    [Fact]
    public void The_inventory_covers_every_hook_payload_and_result_dto()
    {
        Assert.Empty(RemoteConformance.MissingCoverage());
    }

    [Fact]
    public void Every_catalogue_result_family_is_covered_and_consistent()
    {
        foreach (HookContract contract in HookContracts.All)
        {
            Assert.True(
                contract.Kind is HookKind.Observer
                    ? contract.ResultType is null
                    : contract.ResultType is not null,
                $"{contract.Name}: kind and result family disagree"
            );

            if (contract.ResultType is { } family)
            {
                Assert.Contains(family, contract.ResultTypes);
                foreach (Type resultType in contract.ResultTypes)
                {
                    Assert.True(
                        family.IsAssignableFrom(resultType),
                        $"{contract.Name}: {resultType.Name} is not a variant of {family.Name}"
                    );
                }
            }
        }
    }

    [Fact]
    public async Task Every_payload_round_trips_over_the_wire()
    {
        foreach (HookPayloadSample sample in RemoteConformance.Payloads)
        {
            (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
            await using var host = new RemoteWire(a);
            await using var child = new RemoteWire(b);
            var seenMethods = new List<string>();
            child.RequestReceived += (method, parameters, _) =>
            {
                seenMethods.Add(method);
                HookContract contract = HookContracts.Find(method["hook/".Length..])!;
                HookCallEnvelope envelope = Deserialize<HookCallEnvelope>(parameters!.Value);
                object payload = HookResultCodec.DecodePayload(contract, envelope.Payload);
                return ValueTask.FromResult<JsonElement?>(
                    HookResultCodec.EncodePayload(contract, payload)
                );
            };
            host.Start();
            child.Start();

            JsonElement envelope = EncodeEnvelope(sample.Contract, sample.Payload);
            JsonElement? echoed = await host.CallAsync(
                sample.Contract.Method,
                envelope,
                cancellationToken: Ct
            );

            Assert.Equal([sample.Contract.Method], seenMethods);
            object decoded = HookResultCodec.DecodePayload(sample.Contract, echoed!.Value);
            Assert.Equal(
                RemoteConformance.Canonical(
                    HookResultCodec.EncodePayload(sample.Contract, sample.Payload)
                ),
                RemoteConformance.Canonical(HookResultCodec.EncodePayload(sample.Contract, decoded))
            );
        }
    }

    [Fact]
    public async Task Every_result_round_trips_over_the_wire()
    {
        foreach (HookResultSample sample in RemoteConformance.Results)
        {
            (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
            await using var host = new RemoteWire(a);
            await using var child = new RemoteWire(b);
            host.RequestReceived += (method, parameters, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                HookContract contract = HookContracts.Find(method["hook/".Length..])!;
                Assert.Same(sample.Contract, contract);
                // The child side decodes the payload for context, then answers with the sample.
                _ = HookResultCodec.DecodePayload(
                    contract,
                    parameters!.Value.GetProperty("payload")
                );
                return ValueTask.FromResult<JsonElement?>(
                    HookResultCodec.EncodeResult(contract, sample.Result)
                );
            };
            host.Start();
            child.Start();

            JsonElement? result = await child.CallAsync(
                sample.Contract.Method,
                EncodeEnvelope(sample.Contract, FirstPayloadOf(sample.Contract)),
                cancellationToken: Ct
            );

            object decoded = HookResultCodec.DecodeResult(sample.Contract, result!.Value);
            Assert.Equal(
                RemoteConformance.Canonical(
                    HookResultCodec.EncodeResult(sample.Contract, sample.Result)
                ),
                RemoteConformance.Canonical(HookResultCodec.EncodeResult(sample.Contract, decoded))
            );
        }
    }

    [Fact]
    public async Task The_service_call_envelope_round_trips_over_the_wire()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        child.RequestReceived += (method, parameters, _) =>
        {
            Assert.Equal("service-call", method);
            ServiceCallEnvelope envelope = Deserialize<ServiceCallEnvelope>(parameters!.Value);
            return ValueTask.FromResult<JsonElement?>(Serialize(envelope));
        };
        host.Start();
        child.Start();

        var original = new ServiceCallEnvelope(
            "memory",
            "store",
            JsonDocument.Parse("""{"text":"remember"}""").RootElement.Clone()
        );
        JsonElement? result = await host.CallAsync(
            "service-call",
            Serialize(original),
            cancellationToken: Ct
        );
        ServiceCallEnvelope decoded = Deserialize<ServiceCallEnvelope>(result!.Value);

        Assert.Equal(original.Service, decoded.Service);
        Assert.Equal(original.Method, decoded.Method);
        Assert.Equal(
            RemoteConformance.Canonical(original.Arguments!.Value),
            RemoteConformance.Canonical(decoded.Arguments!.Value)
        );
    }

    [Fact]
    public async Task The_call_envelope_carries_run_ids_and_workspace()
    {
        ToolCallingPayload payload = new(
            "run-1",
            "call-1",
            "read",
            JsonDocument.Parse("{}").RootElement.Clone()
        )
        {
            ParentRunId = "parent-1",
        };
        HookContract contract = HookContracts.Find("ToolCalling")!;

        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        HookCallEnvelope? observed = null;
        child.RequestReceived += (_, parameters, _) =>
        {
            observed = Deserialize<HookCallEnvelope>(parameters!.Value);
            return ValueTask.FromResult<JsonElement?>(
                HookResultCodec.EncodeResult(contract, new ToolCallingResult.Proceed(null))
            );
        };
        host.Start();
        child.Start();

        await host.CallAsync(
            contract.Method,
            EncodeEnvelope(contract, payload),
            cancellationToken: Ct
        );

        Assert.NotNull(observed);
        Assert.Equal("run-1", observed!.Call.RunId);
        Assert.Equal("parent-1", observed.Call.ParentRunId);
        Assert.Equal(Workspace, observed.Call.Workspace);
    }

    private static object FirstPayloadOf(HookContract contract) =>
        RemoteConformance.Payloads.First(sample => sample.Contract == contract).Payload;

    private static JsonElement EncodeEnvelope(HookContract contract, object payload)
    {
        object encoded = HookResultCodec.EncodePayload(contract, payload);
        var envelope = new HookCallEnvelope(
            RemoteCallContext.ForPayload(payload, Workspace),
            (JsonElement)encoded
        );
        return Serialize(envelope);
    }

    private static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(
            value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

    private static T Deserialize<T>(JsonElement element) =>
        JsonSerializer.Deserialize<T>(
            element,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        ) ?? throw new InvalidOperationException($"{typeof(T).Name} deserialized to null");
}
