using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>Images in model requests: storage as references, hydration to data, tool-result shape.</summary>
public sealed class AgentImageRequestTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] PngBytes = SessionImageTestData.PngBytes;

    [Fact]
    public async Task A_user_image_is_stored_as_a_reference_and_sent_as_data_content()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("seen"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session, Model = VisionModel() }
        );

        await harness.RunAsync(new UserInput("look", [Image(2, 3)]), Ct).ToListAsync(Ct);

        SessionMessageEntry user = Assert.IsType<SessionMessageEntry>(session.Entries[0]);
        ImageContentReference reference = Assert.IsType<ImageContentReference>(
            user.Message.Contents[1]
        );
        Assert.Equal(SessionImageTestData.PngDigest, reference.Digest);
        Assert.Equal("image/png", reference.MimeType);
        Assert.Equal(2, reference.Width);
        Assert.Equal(3, reference.Height);

        DataContent data = Assert.Single(
            client.Requests[0].Messages[0].Contents.OfType<DataContent>()
        );
        Assert.Equal(PngBytes, data.Data.ToArray());
        Assert.Equal("image/png", data.MediaType);

        string json = File.ReadAllText(session.Path);
        Assert.Contains("\"$type\":\"image\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(PngBytes), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tool_image_appends_a_placeholder_tool_message_then_one_user_image_message()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        ScriptedTool tool = AgentTestSupport.ReadTool("[image 2x3]");
        tool.OnExecute = (_, _) =>
            new ToolResult("[image 2x3]", IsError: false) { Images = [Image(2, 3)] };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "a.png"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(tool),
            new AgentHarnessOptions { Session = session, Model = VisionModel() }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.True(Assert.Single(events.OfType<ToolCallResult>()).HasImages);
        SessionMessageEntry toolEntry = Assert.IsType<SessionMessageEntry>(session.Entries[2]);
        Assert.Equal(ChatRole.Tool, toolEntry.Message.Role);
        Assert.IsType<FunctionResultContent>(Assert.Single(toolEntry.Message.Contents));

        SessionMessageEntry imageEntry = Assert.IsType<SessionMessageEntry>(session.Entries[3]);
        Assert.Equal(ChatRole.User, imageEntry.Message.Role);
        Assert.Equal("Image from read: [image 2x3].", imageEntry.Message.Text);
        Assert.True(
            imageEntry.Message.AdditionalProperties?.ContainsKey(AgentHarness.ToolImageMarker)
                == true
        );
        ImageContentReference reference = Assert.IsType<ImageContentReference>(
            imageEntry.Message.Contents[1]
        );
        Assert.Equal(SessionImageTestData.PngDigest, reference.Digest);

        IReadOnlyList<ChatMessage> next = client.Requests[1].Messages;
        Assert.Equal(4, next.Count);
        ChatMessage toolMessage = Assert.Single(next, message => message.Role == ChatRole.Tool);
        FunctionResultContent result = Assert.IsType<FunctionResultContent>(
            Assert.Single(toolMessage.Contents)
        );
        Assert.Equal("[image 2x3]", result.Result);
        Assert.Empty(toolMessage.Contents.OfType<DataContent>());

        ChatMessage imageMessage = next[^1];
        Assert.Equal(ChatRole.User, imageMessage.Role);
        Assert.Equal("Image from read: [image 2x3].", imageMessage.Text);
        DataContent data = Assert.Single(imageMessage.Contents.OfType<DataContent>());
        Assert.Equal(PngBytes, data.Data.ToArray());
        Assert.Equal("image/png", data.MediaType);

        Session reloaded = Session.Load(session.Path);
        SessionMessageEntry reloadedImage = Assert.IsType<SessionMessageEntry>(reloaded.Entries[3]);
        Assert.True(
            reloadedImage.Message.AdditionalProperties?.ContainsKey(AgentHarness.ToolImageMarker)
                == true
        );
    }

    [Fact]
    public async Task A_resumed_reference_is_rehydrated_without_rewriting_the_session()
    {
        using var temp = new TempDirectory();
        Session created = SessionImageTestData.CreateSession(temp);
        ImageContentReference reference = created.Images.Store(PngBytes, "image/png", 2, 3);
        created.AppendMessage(new ChatMessage(ChatRole.User, [reference]));
        Session loaded = Session.Load(created.Path);
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("ok"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = loaded, Model = VisionModel() }
        );

        await harness.RunAsync("go", Ct).ToListAsync(Ct);

        DataContent data = Assert.Single(
            client.Requests[0].Messages[0].Contents.OfType<DataContent>()
        );
        Assert.Equal(PngBytes, data.Data.ToArray());
        string json = File.ReadAllText(loaded.Path);
        Assert.Contains("\"$type\":\"image\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(PngBytes), json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_blob_fails_when_the_harness_is_built_before_any_request()
    {
        using var temp = new TempDirectory();
        Session created = SessionImageTestData.CreateSession(temp);
        ImageContentReference reference = created.Images.Store(PngBytes, "image/png", 2, 3);
        created.AppendMessage(new ChatMessage(ChatRole.User, [reference]));
        string expected = created.Images.BlobPath(reference);
        File.Delete(expected);
        Session loaded = Session.Load(created.Path);
        var client = new ScriptedChatClient();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new AgentHarness(
                client,
                new ToolRegistry(),
                new AgentHarnessOptions { Session = loaded, Model = VisionModel() }
            )
        );

        Assert.Contains(reference.Digest, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task A_no_session_image_reaches_the_request_from_memory_and_writes_nothing()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("seen"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { WorkingDirectory = temp.Root, Model = VisionModel() }
        );

        await harness.RunAsync(new UserInput("look", [Image(2, 3)]), Ct).ToListAsync(Ct);

        DataContent data = Assert.Single(
            client.Requests[0].Messages[0].Contents.OfType<DataContent>()
        );
        Assert.Equal(PngBytes, data.Data.ToArray());
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Root));
    }

    [Fact]
    public async Task A_resumed_image_on_a_text_only_model_fails_before_any_provider_request()
    {
        using var temp = new TempDirectory();
        Session created = SessionImageTestData.CreateSession(temp);
        ImageContentReference reference = created.Images.Store(PngBytes, "image/png", 2, 3);
        created.AppendMessage(new ChatMessage(ChatRole.User, [reference]));
        Session loaded = Session.Load(created.Path);
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("ok"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = loaded, Model = TextOnlyModel() }
        );

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        RunError error = Assert.Single(events.OfType<RunError>());
        Assert.Contains(
            "model text-only-test does not support images",
            error.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "1 image in the request cannot be sent",
            error.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Use a vision model to continue this session.",
            error.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task A_live_image_on_a_text_only_model_fails_before_any_provider_request()
    {
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("ok"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Model = TextOnlyModel() }
        );

        List<AgentEvent> events = await harness
            .RunAsync(new UserInput("look", [Image(2, 3)]), Ct)
            .ToListAsync(Ct);

        RunError error = Assert.Single(events.OfType<RunError>());
        Assert.Contains(
            "model text-only-test does not support images",
            error.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "1 image in the request cannot be sent",
            error.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Use a vision model to continue this session.",
            error.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(client.Requests);
    }

    private static ModelInfo VisionModel() =>
        new("vision-test", "openai", null, 100000, true) { SupportsVision = true };

    private static ModelInfo TextOnlyModel() => new("text-only-test", "openai", null, 100000, true);

    private static ImageAttachment Image(int width, int height) =>
        new("image/png", width, height, PngBytes);
}
