using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class QuestionDecisionSuggestionTests
{
    [Fact]
    public async Task Suggested_option_is_display_only_when_the_user_skips()
    {
        using var models = new TempDirectory();
        string modelsPath = models.File("models.json");
        File.WriteAllText(
            modelsPath,
            """
            {"schemaVersion":2,"models":[
              {"id":"chat-model","provider":"openai","contextWindow":64000,"supportsTools":true},
              {"id":"decision-model","provider":"openai","contextWindow":64000,"supportsTools":false}
            ]}
            """
        );
        var chat = new ScriptedChatClient()
            .Enqueue(QuestionCall(), Scripts.ToolCalls())
            .Enqueue(Scripts.Text("I will continue without your answer."), Scripts.Stop());
        var decision = new ScriptedChatClient().Enqueue(
            Scripts.Text(
                """{"results":{"question-1":{"answer":"Flexible","probabilities":{"Simple":0.2,"Flexible":0.8}}}}"""
            ),
            Scripts.Stop()
        );
        var factory = new QuestionSuggestionFactory(
            new Dictionary<string, IChatClient>(StringComparer.Ordinal)
            {
                ["chat-model"] = chat,
                ["decision-model"] = decision,
            }
        );
        using var host = new InteractiveSessionHost(factory: factory, modelsPath: modelsPath);
        Configure(host, includeDecision: true, suggest: true);
        SaveProvider(host);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("Choose a plan.");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Which plan should I use?");
        Assert.Contains(
            "Flexible (suggested · decision model 0.80)",
            host.Frames,
            StringComparison.Ordinal
        );
        host.Advance(301);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => chat.Requests.Count == 2);

        ToolCallResult result = Assert.Single(host.Events.OfType<ToolCallResult>());
        Assert.Contains("unanswered: user skipped", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            host.Events.OfType<DecisionEvaluated>(),
            evaluated => evaluated.QuestionId == "question-1" && evaluated.Answer == "Flexible"
        );
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Choosing_a_suggested_option_does_not_approve_a_write()
    {
        using var models = new TempDirectory();
        string modelsPath = models.File("models.json");
        File.WriteAllText(
            modelsPath,
            """
            {"schemaVersion":2,"models":[
              {"id":"chat-model","provider":"openai","contextWindow":64000,"supportsTools":true},
              {"id":"decision-model","provider":"openai","contextWindow":64000,"supportsTools":false}
            ]}
            """
        );
        var client = new ScriptedChatClient()
            .Enqueue(QuestionCall(), Scripts.ToolCalls())
            .Enqueue(
                Scripts.Text(
                    """{"results":{"question-1":{"answer":"Flexible","probabilities":{"Simple":0.2,"Flexible":0.8}}}}"""
                ),
                Scripts.Stop()
            )
            .Enqueue(
                Scripts.Call(
                    "write-call",
                    "write",
                    Scripts.Args(("path", "note.txt"), ("content", "not approved"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        var factory = new QuestionSuggestionFactory(
            new Dictionary<string, IChatClient>(StringComparer.Ordinal)
            {
                ["chat-model"] = client,
                ["decision-model"] = client,
            }
        );
        using var host = new InteractiveSessionHost(factory: factory, modelsPath: modelsPath);
        Configure(host, includeDecision: true, suggest: true);
        SaveProvider(host);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("Choose a plan and write a file.");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Which plan should I use?");
        Assert.Contains(
            "Flexible (suggested · decision model 0.80)",
            host.Frames,
            StringComparison.Ordinal
        );
        host.Advance(301);
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();

        Assert.Equal("write", host.Session.PendingApproval!.ToolName);
        Assert.False(File.Exists(host.Temp.File("note.txt")));
        host.Console.SendText("n");
        await host.WaitUntilAsync(() => client.Requests.Count == 3);
        Assert.False(File.Exists(host.Temp.File("note.txt")));
        host.Console.Complete();
        await run;
    }

    private static ChatResponseUpdate QuestionCall() =>
        Scripts.Call(
            "question-call",
            "ask_user",
            Scripts.Args(
                (
                    "questions",
                    new[]
                    {
                        new
                        {
                            header = "Plan",
                            question = "Which plan should I use?",
                            options = new[]
                            {
                                new { label = "Simple", description = "Fewer moving parts." },
                                new { label = "Flexible", description = "More room to grow." },
                            },
                            multiSelect = false,
                        },
                    }
                )
            )
        );

    private static void Configure(InteractiveSessionHost host, bool includeDecision, bool suggest)
    {
        string decisionModel = includeDecision
            ? ",\n    \"decision\": { \"provider\": \"router\", \"model\": \"decision-model\" }"
            : string.Empty;
        string decisionProfile = includeDecision ? "\"profile\": \"decision\"" : string.Empty;
        string questions = suggest ? "\"questions\": \"suggest\"" : string.Empty;
        string separator = includeDecision && suggest ? ", " : string.Empty;
        File.WriteAllText(
            host.Temp.File("settings.json"),
            $$"""
            {
              "schemaVersion": 2,
              "profiles": {
                "chat": { "provider": "router", "model": "chat-model" }{{decisionModel}}
              },
              "usages": { "chat": "chat" },
              "decisions": { {{decisionProfile}}{{separator}}{{questions}} }
            }
            """
        );
    }

    private static void SaveProvider(InteractiveSessionHost host)
    {
        bool valid = ProviderEntry.TryCreate(
            "router",
            "openai",
            new Uri("https://provider.example.test/v1"),
            "auth:router",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );
        Assert.True(valid, error);
        ProviderStore.Save(
            AuthStore.ProviderStoreFilePath(host.Temp.File("auth.json")),
            [Assert.IsType<ProviderEntry>(provider)]
        );
    }

    private sealed class QuestionSuggestionFactory(IReadOnlyDictionary<string, IChatClient> clients)
        : IChatClientFactory
    {
        public IChatClient Create(ModelInfo model) => clients[model.Id];

        public IChatClient Create(ModelInfo model, string? apiKey) => Create(model);
    }
}
