using Lunate.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lunate.Agent.Tests;

internal static class TestPipeline
{
    internal static IChatClient Create(IChatClient provider, bool enableOpenTelemetry = true) =>
        new ChatClientFactory(
            NullLoggerFactory.Instance,
            enableOpenTelemetry: enableOpenTelemetry,
            providerClientFactory: _ => provider
        ).Create(new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true));
}
