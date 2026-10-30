using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class ContextMaskingCompositionTests
{
    private const string ProviderKey = "sk-live-context-hygiene-provider-187";
    private const string UnknownKey = "sk-unheld-context-hygiene-value";
    private const string Marker = "[redacted:OPENAI_API_KEY]";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Print_mode_masks_an_environment_provider_key_with_and_without_a_session(
        bool noSession
    )
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("key.txt"), ProviderKey + "\n" + UnknownKey + "\n");
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("read-key", "read", Scripts.Args(("path", "key.txt"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("Done"), Scripts.Stop());
        Func<string, string?> environment = PrintModeTestSupport.Environment();
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Read the credential file",
            Json = true,
            Launch = new LaunchOptions { NoSession = noSession },
            Environment = name => name == "OPENAI_API_KEY" ? ProviderKey : environment(name),
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        Assert.DoesNotContain(ProviderKey, output, StringComparison.Ordinal);
        string eventOutput = PrintModeTestSupport
            .ParseLines(output)
            .Single(line => PrintModeTestSupport.TypeOf(line) == "tool_call_result")
            .GetProperty("output")
            .GetString()!;
        Assert.Contains(Marker, eventOutput, StringComparison.Ordinal);
        Assert.Contains(UnknownKey, eventOutput, StringComparison.Ordinal);
        string requestOutput = client
            .Requests[1]
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single()
            .Result!.ToString()!;
        Assert.Contains(Marker, requestOutput, StringComparison.Ordinal);
        Assert.Contains(UnknownKey, requestOutput, StringComparison.Ordinal);

        string[] sessionFiles = Directory.Exists(temp.File("sessions"))
            ? Directory.GetFiles(temp.File("sessions"), "*.jsonl")
            : [];
        if (noSession)
        {
            Assert.Empty(sessionFiles);
            return;
        }

        string sessionText = File.ReadAllText(Assert.Single(sessionFiles));
        Assert.Contains(Marker, sessionText, StringComparison.Ordinal);
        Assert.Contains(UnknownKey, sessionText, StringComparison.Ordinal);
        Assert.DoesNotContain(ProviderKey, sessionText, StringComparison.Ordinal);
    }
}
