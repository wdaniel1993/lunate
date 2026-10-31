using System.Reflection;
using Lunate.Contracts;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class DecisionServiceContractTests
{
    [Fact]
    public void The_abstractions_service_references_only_the_bottom_contract()
    {
        Assembly assembly = typeof(IDecisionService).Assembly;

        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Lunate.", StringComparison.Ordinal) == true
                && reference.Name != "Lunate.Contracts"
        );
        Assert.All(
            typeof(IDecisionService).GetMethods().SelectMany(method =>
                new[] { method.ReturnType }.Concat(method.GetParameters().Select(parameter => parameter.ParameterType))
            ),
            type =>
                Assert.DoesNotContain(
                    new[] { "Provider", "Tool", "Loop" },
                    marker => type.FullName?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true
                )
        );
    }

    [Fact]
    public void An_extension_context_resolves_the_core_decision_service()
    {
        using var temp = new TempDirectory();
        var service = new TestDecisionService();
        var loader = new ExtensionLoader(
            new ExtensionHostOptions { StorePath = temp.Root }
        );
        loader.Services.RegisterCore("core/decision", service);
        var context = new ExtensionContext(
            "test-extension",
            settings: null!,
            secrets: null!,
            log: new RecordingExtensionLog(),
            findCoreService: loader.Services.FindCore
        );

        Assert.True(context.TryGetCoreService<IDecisionService>("core/decision", out var resolved));
        Assert.Same(service, resolved);
    }

    private sealed class TestDecisionService : IDecisionService
    {
        public Task<DecisionResponse> DecideAsync(
            DecisionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
