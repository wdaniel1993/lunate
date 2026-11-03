using Lunate.Ai.Decisions;
using Lunate.Ai.SystemOne;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class SystemOneChatFacadeTests
{
    [Fact]
    public async Task GetResponseAsync_refuses_chat_for_a_decision_model()
    {
        SystemOneChatFacade client = CreateClient();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                client.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "hello")],
                    cancellationToken: TestContext.Current.CancellationToken
                )
        );

        Assert.Contains("typesafe/jev-1.14 is a decision model", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetStreamingResponseAsync_refuses_chat_for_a_decision_model()
    {
        SystemOneChatFacade client = CreateClient();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")],
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("typesafe/jev-1.14 is a decision model", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetService_exposes_the_decision_client()
    {
        SystemOneDecisionClient decisionClient = new(
            new HttpClient(),
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
        );
        SystemOneChatFacade client = new("typesafe/jev-1.14", decisionClient);

        Assert.Same(decisionClient, client.GetService(typeof(IDecisionClient)));
    }

    private static SystemOneChatFacade CreateClient() =>
        new(
            "typesafe/jev-1.14",
            new SystemOneDecisionClient(
                new HttpClient(),
                new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
            )
        );
}
