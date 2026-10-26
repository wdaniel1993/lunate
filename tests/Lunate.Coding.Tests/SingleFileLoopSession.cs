using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The small shared surface of the phase-4 session tests: the wait window for anything that runs a
/// real compile, the approval policy these runs need, and the one step every loop test shares —
/// the model writes the file and the user approves the reload.
/// </summary>
internal static class SingleFileLoopSession
{
    /// <summary>
    /// The window for waits that depend on a compile: two Roslyn compiles against a reference
    /// pack run inside these tests, and a cold CI runner needs more than a frame's 5 s.
    /// </summary>
    internal static readonly TimeSpan CompileWait = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The session's default approval policy asks before a write; these tests are about the
    /// extension loop, so the policy is the auto-edit one (writes inside the workspace run).
    /// </summary>
    internal static void AutoEdit(InteractiveSessionHost host) =>
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "approval": "auto-edit"
            }
            """
        );

    /// <summary>
    /// Turn one and the user's answer: the model writes the file, the user asks for the reload, the
    /// question is approved, and the reload reports the build it loaded.
    /// </summary>
    internal static async Task WriteAndApproveReloadAsync(
        InteractiveSessionHost host,
        SingleFileLoopFixture fixture,
        string source
    )
    {
        host.Console.SendText("write the extension");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => File.Exists(fixture.SourcePath), CompileWait);
        // The workspace has no .editorconfig, so a new file follows the platform default: the
        // expectation names that default instead of assuming POSIX.
        Assert.Equal(
            source.ReplaceLineEndings(Environment.NewLine),
            File.ReadAllText(fixture.SourcePath)
        );

        host.Console.SendText("/extensions reload acme-tools");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "Trust the single-file extension 'acme-tools' 1.0.0",
            CompileWait
        );
        host.Console.SendText("y");
        await host.WaitForFrameAsync("extension 'acme-tools' reloaded — build ", CompileWait);
    }
}
