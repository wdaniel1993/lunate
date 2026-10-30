using Acp.Schema;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Coding;
using Lunate.Protocols.Acp;
using FunctionResultContent = Microsoft.Extensions.AI.FunctionResultContent;
using IChatClient = Microsoft.Extensions.AI.IChatClient;

namespace Lunate.Protocols.Tests;

public sealed class AcpQuestionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ask_user_without_a_non_approval_input_mechanism_is_unanswered_without_permission()
    {
        using var temp = new TempDirectory();
        using var workspaceServices = new WorkspaceServices(4);
        var model = new AcpScriptedChatClient()
            .Enqueue(
                AcpScripts.Call(
                    "question-1",
                    "ask_user",
                    new Dictionary<string, object?>
                    {
                        ["questions"] = new[]
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
                        },
                    }
                ),
                AcpScripts.ToolCalls()
            )
            .Enqueue(
                AcpScripts.Text("I'll assume the simple plan and continue."),
                AcpScripts.Stop()
            );
        await using AcpRuntime runtime = AcpTestSupport.Start(context =>
        {
            Assert.NotNull(context.ClientApprover);
            return AcpMode.CreateHarness(
                context,
                host: null,
                mcpTools: [],
                inheritedTools: [],
                workspaceServices,
                new LaunchOptions { Model = "gpt-4o-mini" },
                new AcpChatClientFactory(model),
                temp.File("sessions")
            );
        });

        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        runtime.State.PermissionHandler = (_, _) =>
            Task.FromResult(
                new RequestPermissionResponse { Outcome = new CancelledPermissionOutcome() }
            );
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        PromptResponse response = await runtime
            .Client.PromptAsync(
                new PromptRequest
                {
                    SessionId = session.SessionId,
                    Prompt = [new TextContent { Text = "Choose a plan." }],
                },
                Ct
            )
            .WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Empty(runtime.State.PermissionRequests);
        FunctionResultContent result = Assert.Single(
            model
                .Requests[1]
                .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
        );
        Assert.Equal(
            "unanswered: no user present. State an assumption and continue.",
            result.Result?.ToString()
        );
    }

    private sealed class AcpChatClientFactory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create(ModelInfo model) => client;

        public IChatClient Create(ModelInfo model, string? apiKey) => client;
    }
}
