namespace Lunate.Coding.Tests;

/// <summary>
/// A redirected or sandboxed run is never invisible: when <c>LUNATE_HOME</c> moves the state root,
/// both surfaces — <c>lunate --version</c> and the interactive welcome header — print the active home
/// in one spelling; when it is unset, neither does.
/// <para>
/// The proof runs in a child process because the variable is process-wide. The child sets and clears
/// it with try/finally in a process no other test is using.
/// </para>
/// </summary>
public sealed class LunateHomeVisibilityTests
{
    private const string RootVariable = "LUNATE_HOME";

    [Fact]
    public async Task The_active_home_is_visible_on_both_surfaces_and_absent_when_unset()
    {
        const string Method = nameof(
            The_active_home_is_visible_on_both_surfaces_and_absent_when_unset
        );
        if (TestChildProcess.IsChild(Method))
        {
            await ProbeAsync();
            return;
        }

        (int exitCode, string output) = await TestChildProcess.RunAsync(
            typeof(LunateHomeVisibilityTests),
            Method
        );
        Assert.True(
            exitCode == 0,
            $"the visibility probe failed (exit {exitCode}):{Environment.NewLine}{output}"
        );
    }

    private static async Task ProbeAsync()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string state = temp.File("state");
        string expected = string.Concat("home ", state, " (", RootVariable, ")");

        Environment.SetEnvironmentVariable(RootVariable, state);
        try
        {
            string[] version = VersionLines();
            Assert.Equal(2, version.Length);
            Assert.Equal(ProductVersion.Informational, version[0]);
            Assert.Equal(expected, version[1]);

            string header = await ComposeHeaderAsync(worktree);
            Assert.Contains(expected, header, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RootVariable, null);
        }

        // Unset: the default root is not advertised, and nothing about the redirect lingers.
        string[] plain = VersionLines();
        Assert.Equal([ProductVersion.Informational], plain);

        string plainHeader = await ComposeHeaderAsync(worktree);
        Assert.DoesNotContain(
            string.Concat("(", RootVariable, ")"),
            plainHeader,
            StringComparison.Ordinal
        );
    }

    private static string[] VersionLines()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(0, Cli.Run(["--version"], output, errors));
        Assert.Empty(errors.ToString());
        return
        [
            .. output
                .ToString()
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries),
        ];
    }

    /// <summary>
    /// The real session path: the header is printed from the session's own compose step, so the line
    /// cannot be a renderer-only decoration. The host's explicit temp paths keep the proof from
    /// touching real state while the header reads the same process environment.
    /// </summary>
    private static async Task<string> ComposeHeaderAsync(string worktree)
    {
        using var host = new InteractiveSessionHost(workingDirectory: worktree);
        // The committed header is wrapped at the scrollback's width; a temp path is long, and the
        // assertion is about the whole line, not Spectre's wrapping.
        host.Scrollback.Profile.Width = 400;
        host.Console.Complete();
        await host.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
        string scrollback = host.Scrollback.Output;
        Assert.Contains("lunate", scrollback, StringComparison.Ordinal);
        return scrollback;
    }
}
