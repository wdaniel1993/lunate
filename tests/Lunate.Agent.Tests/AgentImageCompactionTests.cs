using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>Image references in compaction: placeholder rendering, token estimate, blob survival.</summary>
public sealed class AgentImageCompactionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] PngBytes = SessionImageTestData.PngBytes;
    private const int Window = 1000;

    [Fact]
    public void The_summarization_transcript_renders_an_image_as_its_placeholder_without_bytes()
    {
        ChatMessage message = new(
            ChatRole.User,
            [
                new TextContent("see"),
                new ImageContentReference(SessionImageTestData.PngDigest, "image/png", 2, 3),
            ]
        );

        List<ChatMessage> request = CompactionReducer.BuildSummarizationRequest([message]);

        string transcript = request[1].Text;
        Assert.Contains("[image 2x3]", transcript, StringComparison.Ordinal);
        Assert.Empty(request[1].Contents.OfType<DataContent>());
        Assert.DoesNotContain(
            Convert.ToBase64String(PngBytes),
            transcript,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void An_image_adds_at_least_the_minimum_256_estimated_tokens_to_the_length()
    {
        ImageContentReference reference = new(SessionImageTestData.PngDigest, "image/png", 2, 3);
        ChatMessage without = new(ChatRole.User, [new TextContent("see")]);
        ChatMessage with = new(ChatRole.User, [new TextContent("see"), reference]);

        long difference =
            CompactionReducer.Utf8Length(with) - CompactionReducer.Utf8Length(without);

        Assert.True(
            difference >= 1024,
            $"expected at least 1024 bytes, got {difference.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        );
    }

    [Fact]
    public async Task Compaction_sends_the_placeholder_and_keeps_the_session_blob()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        ImageContentReference reference = session.Images.Store(PngBytes, "image/png", 4000, 4000);
        session.AppendMessage(new ChatMessage(ChatRole.User, [new TextContent("look"), reference]));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "seen"));
        AppendTurn(session, "two");
        AppendTurn(session, "three");
        AppendTurn(session, "four");
        AppendTurn(session, "five");
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text("summary"))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                Session = session,
                ModelId = TestCatalog.ModelId,
                ModelCatalog = TestCatalog.WithWindow(Window),
            }
        );

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        Assert.Single(events.OfType<CompactionApplied>());
        ScriptedRequest summarization = client.Requests[0];
        string transcript = summarization.Messages[1].Text;
        Assert.Contains("[image 4000x4000]", transcript, StringComparison.Ordinal);
        Assert.Empty(summarization.Messages[1].Contents.OfType<DataContent>());
        Assert.DoesNotContain(
            Convert.ToBase64String(PngBytes),
            transcript,
            StringComparison.Ordinal
        );
        Assert.True(File.Exists(session.Images.BlobPath(reference)));
    }

    private static void AppendTurn(Session session, string text)
    {
        session.AppendMessage(new ChatMessage(ChatRole.User, text));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted " + text));
    }
}
