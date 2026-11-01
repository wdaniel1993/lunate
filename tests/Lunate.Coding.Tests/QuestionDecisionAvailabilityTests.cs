using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class QuestionDecisionAvailabilityTests
{
    [Fact]
    public async Task Questions_off_does_not_evaluate_with_a_profile_configured()
    {
        using var models = new TempDirectory();
        string modelsPath = WriteModels(models);
        var chat = new ScriptedChatClient()
            .Enqueue(QuestionCall(), Scripts.ToolCalls())
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        var factory = Factory(chat, new ScriptedChatClient(), includeDecision: true);
        using var host = new InteractiveSessionHost(factory: factory, modelsPath: modelsPath);
        Configure(host, profile: true, suggest: false);
        SaveProvider(host);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("Choose a plan.");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Which plan should I use?");
        Assert.Empty(host.Events.OfType<DecisionEvaluated>());
        Assert.Empty(host.Events.OfType<DecisionUnavailable>());
        host.Advance(301);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => chat.Requests.Count == 2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Suggestions_without_a_profile_keep_the_question_available_without_an_error()
    {
        using var models = new TempDirectory();
        string modelsPath = WriteModels(models);
        var chat = new ScriptedChatClient()
            .Enqueue(QuestionCall(), Scripts.ToolCalls())
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        var factory = Factory(chat, new ScriptedChatClient(), includeDecision: false);
        using var host = new InteractiveSessionHost(factory: factory, modelsPath: modelsPath);
        Configure(host, profile: false, suggest: true);
        SaveProvider(host);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("Choose a plan.");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Which plan should I use?");
        Assert.DoesNotContain("(suggested · decision model", host.Frames, StringComparison.Ordinal);
        await host.WaitUntilAsync(() =>
            host
                .Events.OfType<DecisionUnavailable>()
                .Any(unavailable => unavailable.QuestionId == "question-1")
        );
        Assert.Contains(
            host.Events.OfType<DecisionUnavailable>(),
            unavailable => unavailable.QuestionId == "question-1"
        );
        Assert.Empty(host.Events.OfType<DecisionFailed>());
        host.Advance(301);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => chat.Requests.Count == 2);
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

    private static string WriteModels(TempDirectory files)
    {
        string path = files.File("models.json");
        File.WriteAllText(
            path,
            """
            {"schemaVersion":2,"models":[
              {"id":"chat-model","provider":"openai","contextWindow":64000,"supportsTools":true},
              {"id":"decision-model","provider":"openai","contextWindow":64000,"supportsTools":false}
            ]}
            """
        );
        return path;
    }

    private static void Configure(InteractiveSessionHost host, bool profile, bool suggest)
    {
        string decisionModel = profile
            ? ",\n    \"decision\": { \"provider\": \"router\", \"model\": \"decision-model\" }"
            : string.Empty;
        string settings =
            profile && suggest ? "\"profile\": \"decision\", \"questions\": \"suggest\""
            : profile ? "\"profile\": \"decision\""
            : "\"questions\": \"suggest\"";
        File.WriteAllText(
            host.Temp.File("settings.json"),
            $$"""
            {
              "schemaVersion": 2,
              "profiles": { "chat": { "provider": "router", "model": "chat-model" }{{decisionModel}} },
              "usages": { "chat": "chat" },
              "decisions": { {{settings}} }
            }
            """
        );
    }

    private static QuestionSuggestionFactory Factory(
        ScriptedChatClient chat,
        ScriptedChatClient decision,
        bool includeDecision
    )
    {
        Dictionary<string, IChatClient> clients = new(StringComparer.Ordinal)
        {
            ["chat-model"] = chat,
        };
        if (includeDecision)
        {
            clients["decision-model"] = decision;
        }

        return new QuestionSuggestionFactory(clients);
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
