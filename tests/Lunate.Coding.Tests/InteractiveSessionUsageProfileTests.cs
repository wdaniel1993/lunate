using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionUsageProfileTests
{
    [Fact]
    public async Task Interactive_decision_profile_lists_decide_in_the_prompt_and_tool_declarations()
    {
        using var temp = new TempDirectory();
        string modelsPath = temp.File("models.json");
        File.WriteAllText(
            modelsPath,
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "chat-model", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true },
                { "id": "decision-model", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": false }
              ]
            }
            """
        );
        var chat = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Stop }
        );
        var factory = new UsageFactory(chat);
        using var host = new InteractiveSessionHost(factory: factory, modelsPath: modelsPath);
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "chat-profile": {
                  "provider": "chat-provider",
                  "model": "chat-model"
                },
                "decision-profile": {
                  "provider": "chat-provider",
                  "model": "decision-model"
                }
              },
              "usages": {
                "chat": "chat-profile",
                "decision": "decision-profile"
              }
            }
            """
        );
        ProviderStore.Save(
            AuthStore.ProviderStoreFilePath(host.Temp.File("auth.json")),
            [Provider("chat-provider", "openai")]
        );
        Task run = host.RunAsync();

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => chat.Requests.Count > 0);

        ScriptedRequest request = chat.Requests[0];
        Assert.Contains("decide", request.Messages[0].Text, StringComparison.Ordinal);
        Assert.Contains(request.Options!.Tools!, declaration => declaration.Name == "decide");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Interactive_chat_uses_the_configured_profile_and_emits_attribution_once()
    {
        using var temp = new TempDirectory();
        string modelsPath = temp.File("models.json");
        File.WriteAllText(
            modelsPath,
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "chat-model", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true }
              ]
            }
            """
        );
        var chat = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new TextContent("answer"),
                    new UsageContent(
                        new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3 }
                    ),
                ]
            )
            {
                FinishReason = ChatFinishReason.Stop,
            }
        );
        var factory = new UsageFactory(chat);
        using var host = new InteractiveSessionHost(factory: factory, modelsPath: modelsPath);
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "chat-profile": {
                  "provider": "chat-provider",
                  "model": "chat-model"
                }
              },
              "usages": { "chat": "chat-profile" }
            }
            """
        );
        ProviderStore.Save(
            AuthStore.ProviderStoreFilePath(host.Temp.File("auth.json")),
            [Provider("chat-provider", "openai")]
        );
        Task run = host.RunAsync();

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Events.OfType<UsageUpdated>().Any());

        UsageUpdated usage = Assert.Single(host.Events.OfType<UsageUpdated>());
        Assert.Equal("chat-model", usage.ModelId);
        Assert.Equal(
            new ModelUsageAttribution("chat", "chat-profile", "chat-provider", "openai"),
            usage.Attribution
        );
        Assert.Single(factory.Models);
        Assert.Equal("chat-model", factory.Models[0].Id);
        Assert.Equal(new Uri("https://provider.example.test/v1"), factory.Models[0].Endpoint);

        host.Console.Complete();
        await run;
    }

    private static ProviderEntry Provider(string name, string kind)
    {
        bool valid = ProviderEntry.TryCreate(
            name,
            kind,
            new Uri("https://provider.example.test/v1"),
            $"auth:{name}",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }

    private sealed class UsageFactory(ScriptedChatClient client) : IChatClientFactory
    {
        public List<ModelInfo> Models { get; } = [];

        public IChatClient Create(ModelInfo model)
        {
            Models.Add(model);
            return client;
        }

        public IChatClient Create(ModelInfo model, string? apiKey) => Create(model);
    }
}
