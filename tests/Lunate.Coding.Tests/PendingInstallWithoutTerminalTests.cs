using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// Print and ACP runs have no terminal to ask at, and the default prompt refuses rather than
/// guesses: an installed extension stays pending and does not load. The trust question stands
/// between install and execution, which is the guarantee the addendum claims — not the session
/// marker, which only makes an install visible in the command the user approves.
/// </summary>
public sealed class PendingInstallWithoutTerminalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_installed_extension_does_not_load_with_no_terminal_to_ask_at()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle();

        int installed = feed.Run(["Acme.Toolkit@1.4.2"], out string output, out string errors);
        Assert.Equal(0, installed);
        Assert.Empty(errors);
        Assert.Contains("installed", output, StringComparison.Ordinal);

        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(new ExtensionHostOptions { StorePath = feed.Store });
        loader.Discover(feed.Worktree, feed.Worktree);

        // Exactly the prompt print and ACP mode compose when none is injected.
        var prompt = new NonInteractiveTrustPrompt(log);

        ExtensionTrustDeniedException failure =
            await Assert.ThrowsAsync<ExtensionTrustDeniedException>(() =>
                loader.Load("acme-tools", feed.Worktree, feed.Worktree, prompt, Ct)
            );

        Assert.Contains("was not trusted", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "trust")));
        Assert.Single(log.Messages);
        Assert.Contains(
            "extension 'acme-tools' installed at the user scope was not trusted",
            log.Messages[0],
            StringComparison.Ordinal
        );
    }
}
