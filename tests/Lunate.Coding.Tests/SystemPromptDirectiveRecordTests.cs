using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// Recording and replay of the system-prompt directives (design D5): a fresh session records the
/// <c>system</c> and <c>system-append</c> sections, a resume applies them without flags, and an
/// explicit CLI directive wins over the recorded one.
/// </summary>
public sealed class SystemPromptDirectiveRecordTests
{
    [Fact]
    public async Task A_fresh_print_session_records_both_directive_sections()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions
            {
                SystemPrompt = "the caller's contract",
                AppendSystemPrompt = "one more rule",
            },
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Session session = LoadOnlySession(temp);
        SessionPromptSectionEntry[] sections =
        [
            .. session.Entries.OfType<SessionPromptSectionEntry>(),
        ];
        Assert.Collection(
            sections,
            entry =>
            {
                Assert.Equal("system", entry.Section);
                Assert.Equal("the caller's contract", entry.Text);
            },
            entry =>
            {
                Assert.Equal("system-append", entry.Section);
                Assert.Equal("one more rule", entry.Text);
            }
        );
    }

    [Fact]
    public async Task Resume_replays_a_recorded_replacement_without_flags()
    {
        using var temp = new TempDirectory();
        Session seeded = SeedSession(temp, "remembered question");
        seeded.AppendPromptSection("system", "the recorded contract");
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Session = seeded.Path },
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("the recorded contract", client.Requests[0][0].Text);
    }

    [Fact]
    public async Task Resume_replays_a_recorded_append_after_the_composed_prompt()
    {
        using var temp = new TempDirectory();
        Session seeded = SeedSession(temp, "remembered question");
        seeded.AppendPromptSection("system-append", "the recorded appendix");
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Session = seeded.Path },
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        string system = client.Requests[0][0].Text;
        Assert.EndsWith("\n\nthe recorded appendix", system, StringComparison.Ordinal);
        Assert.Contains("Read a file before you edit it.", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cli_replacement_overrides_the_recorded_replacement()
    {
        using var temp = new TempDirectory();
        Session seeded = SeedSession(temp, "remembered question");
        seeded.AppendPromptSection("system", "the recorded contract");
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Session = seeded.Path, SystemPrompt = "the CLI contract" },
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("the CLI contract", client.Requests[0][0].Text);
    }

    [Fact]
    public async Task A_cli_append_overrides_the_recorded_append()
    {
        using var temp = new TempDirectory();
        Session seeded = SeedSession(temp, "remembered question");
        seeded.AppendPromptSection("system-append", "the recorded appendix");
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions
            {
                Session = seeded.Path,
                AppendSystemPrompt = "the CLI appendix",
            },
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        string system = client.Requests[0][0].Text;
        Assert.EndsWith("\n\nthe CLI appendix", system, StringComparison.Ordinal);
        Assert.DoesNotContain("the recorded appendix", system, StringComparison.Ordinal);
    }

    private static Session SeedSession(TempDirectory temp, string text)
    {
        string directory = temp.File("sessions");
        Directory.CreateDirectory(directory);
        string provisional = Path.Combine(directory, Path.GetRandomFileName());
        Session created = Session.Create(
            provisional,
            new Workspace(temp.Root).WorktreeRoot,
            null,
            new Workspace(temp.Root).WorktreeRoot
        );
        created.AppendMessage(new ChatMessage(ChatRole.User, text));
        string path = Path.Combine(directory, SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        return Session.Load(path);
    }

    private static Session LoadOnlySession(TempDirectory temp) =>
        Session.Load(Assert.Single(Directory.GetFiles(temp.File("sessions"), "*.jsonl")));
}
