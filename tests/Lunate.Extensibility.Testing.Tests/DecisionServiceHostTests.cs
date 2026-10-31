using Lunate.Contracts;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;

namespace Lunate.Extensibility.Testing.Tests;

public sealed class DecisionServiceHostTests
{
    [Fact]
    public async Task Test_host_registers_the_decision_service_for_extensions()
    {
        using var temp = new TempDirectory();
        var service = new TestDecisionService();
        await using var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "decision-host",
                DecisionService = service,
                SourceFiles = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["extension.json"] =
                        """{"id":"decision-host","version":"0.1.0","apiVersion":"^1.15.0","entryAssembly":"test.dll"}""",
                },
            }
        );

        Assert.True(host.Runner.Services.TryGetCore<IDecisionService>("core/decision", out var resolved));
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
