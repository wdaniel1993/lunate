using Lunate.Agent;
using Lunate.Protocols.Acp;

namespace Lunate.Coding.Tests;

/// <summary>
/// The ACP frontend's side of design D5: every <c>session/new</c> creates a fresh session, records
/// the launch prompt directives on it, and composes the harness with them.
/// </summary>
public sealed class AcpSystemPromptTests
{
    [Fact]
    public async Task An_acp_session_composes_the_launch_replacement()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var workspace = new Workspace(temp.Root);
        using var services = new WorkspaceServices(4);
        AgentHarness harness = AcpMode.CreateHarness(
            new AcpSessionContext(workspace.WorktreeRoot, null, null),
            null,
            [],
            [],
            services,
            new LaunchOptions { SystemPrompt = "the acp contract" },
            new FakeChatClientFactory(client),
            temp.File("sessions")
        );

        await foreach (
            AgentEvent _ in harness.RunAsync("Hi", TestContext.Current.CancellationToken)
        ) { }

        Assert.Equal("the acp contract", client.Requests[0][0].Text);
    }

    [Fact]
    public void An_acp_session_records_both_directive_sections()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient();
        var workspace = new Workspace(temp.Root);
        using var services = new WorkspaceServices(4);
        _ = AcpMode.CreateHarness(
            new AcpSessionContext(workspace.WorktreeRoot, null, null),
            null,
            [],
            [],
            services,
            new LaunchOptions
            {
                SystemPrompt = "the acp contract",
                AppendSystemPrompt = "the acp appendix",
            },
            new FakeChatClientFactory(client),
            temp.File("sessions")
        );

        Session session = Session.Load(
            Assert.Single(Directory.GetFiles(temp.File("sessions"), "*.jsonl"))
        );
        SessionPromptSectionEntry[] sections =
        [
            .. session.Entries.OfType<SessionPromptSectionEntry>(),
        ];
        Assert.Collection(
            sections,
            entry =>
            {
                Assert.Equal("system", entry.Section);
                Assert.Equal("the acp contract", entry.Text);
            },
            entry =>
            {
                Assert.Equal("system-append", entry.Section);
                Assert.Equal("the acp appendix", entry.Text);
            }
        );
    }
}
