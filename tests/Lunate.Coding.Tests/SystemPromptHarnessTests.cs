using System.Runtime.CompilerServices;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class SystemPromptHarnessTests
{
    [Fact]
    public async Task Composed_prompt_leads_the_model_request_as_its_system_message()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        string prompt = SystemPrompt.Compose(workspace, ["read"], new ShellResolver("/bin/bash"));
        var client = new CapturingChatClient();
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { SystemPrompt = prompt }
        );

        await foreach (
            AgentEvent _ in harness.RunAsync("Hi", TestContext.Current.CancellationToken)
        ) { }

        IReadOnlyList<ChatMessage> request = Assert.Single(client.Requests);
        Assert.Equal(ChatRole.System, request[0].Role);
        Assert.Equal(prompt, request[0].Text);
        Assert.Equal(ChatRole.User, request[1].Role);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            Requests.Add([.. messages]);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.Stop,
            };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
