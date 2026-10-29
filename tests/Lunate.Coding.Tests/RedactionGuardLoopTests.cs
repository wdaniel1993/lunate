using System.Security.Cryptography;
using System.Text;
using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The sequence the guard exists for (issue 187, part 1): the model reads a <c>.env</c> holding a
/// real key, gets the named placeholder in its context, and returns the file with the placeholder
/// in the key's position. The write is refused and the file is byte-identical afterwards.
/// </summary>
public sealed class RedactionGuardLoopTests
{
    private const string Secret = "sk-ant-abcdef-123456";
    private const string Marker = "[redacted:ANTHROPIC_API_KEY]";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Writing_the_placeholder_back_over_the_real_value_is_refused_and_the_file_is_byte_identical()
    {
        string original = $"ANTHROPIC_API_KEY={Secret}\nTOKEN=plain\n";
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(".env"), original);
        var redactor = new CredentialRedactor([("ANTHROPIC_API_KEY", Secret)]);
        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(temp.Root),
            redaction: redactor
        );
        Session session = Session.Create(temp.File("sessions/s.jsonl"), temp.Root);
        session.Redactor = redactor;
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "read", Scripts.Args(("path", ".env"))),
                Scripts.ToolCalls()
            )
            .Enqueue(
                Scripts.Call(
                    "call-2",
                    "write",
                    Scripts.Args(
                        ("path", ".env"),
                        ("content", $"ANTHROPIC_API_KEY={Marker}\nTOKEN=plain\n")
                    )
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("The write was refused."), Scripts.Stop());
        var harness = new AgentHarness(
            client,
            tools,
            new AgentHarnessOptions
            {
                WorkingDirectory = temp.Root,
                // The mask is the harness's admission step now: the session redactor is wired by
                // the host, so the loop keeps masking without a hidden fallback.
                Session = session,
                ToolOutputMask = redactor.Redact,
            }
        );
        List<AgentEvent> events = [];

        await foreach (AgentEvent agentEvent in harness.RunAsync("read the env, keep it safe", Ct))
        {
            events.Add(agentEvent);
        }

        // The read placed the named placeholder, not the value, into the context.
        string context = TextResultsOf(client.Requests[1]);
        Assert.Contains(Marker, context, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, context, StringComparison.Ordinal);

        // The write-back is refused, naming the credential.
        ToolCallResult write = Assert.Single(
            events.OfType<ToolCallResult>(),
            call => call.CallId == "call-2"
        );
        Assert.True(write.IsError);
        Assert.Contains(Marker, write.Output, StringComparison.Ordinal);

        // The file is byte-identical: a hash comparison, not a text comparison.
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(original)),
            SHA256.HashData(File.ReadAllBytes(temp.File(".env")))
        );
    }

    private static string TextResultsOf(IReadOnlyList<ChatMessage> messages) =>
        string.Join(
            "\n",
            messages
                .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
                .Select(result => result.Result?.ToString() ?? string.Empty)
        );
}
