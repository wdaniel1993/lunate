using Acp.Schema;

namespace Lunate.Protocols.Tests;

public sealed class AcpResourceLinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resource_links_become_readable_text_in_prompt_order()
    {
        using var temp = new TempDirectory();
        using var outside = new TempDirectory();
        File.WriteAllText(temp.File("rel.txt"), "x");
        File.WriteAllText(outside.File("ext.txt"), "y");
        var model = new AcpScriptedChatClient().Enqueue(AcpScripts.Text("ok"), AcpScripts.Stop());
        await using AcpRuntime runtime = AcpTestSupport.Start(_ => AcpTestSupport.Harness(model));
        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );
        string outsideUri = new Uri(outside.File("ext.txt")).AbsoluteUri;

        PromptResponse response = await runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt =
                [
                    new TextContent { Text = "open" },
                    new ResourceLinkContent
                    {
                        Name = "rel.txt",
                        Uri = new Uri(temp.File("rel.txt")).AbsoluteUri,
                    },
                    new ResourceLinkContent { Name = "Outside", Uri = outsideUri },
                    new ResourceLinkContent { Name = "Docs", Uri = "https://example.com/docs" },
                    new TextContent { Text = "please" },
                ],
            },
            Ct
        );

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(
            $"open\n@rel.txt\nOutside ({outsideUri})\nDocs (https://example.com/docs)\nplease",
            model.LastUserText
        );
    }

    [Fact]
    public async Task A_prompt_of_only_resource_links_reaches_the_model()
    {
        using var temp = new TempDirectory();
        var model = new AcpScriptedChatClient().Enqueue(AcpScripts.Text("ok"), AcpScripts.Stop());
        await using AcpRuntime runtime = AcpTestSupport.Start(_ => AcpTestSupport.Harness(model));
        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        PromptResponse response = await runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new ResourceLinkContent { Name = "Docs", Uri = "https://example.com" }],
            },
            Ct
        );

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal("Docs (https://example.com)", model.LastUserText);
    }
}
