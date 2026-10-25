namespace Lunate.Coding.Tests;

/// <summary>
/// The trust guard at the surfaces that know a workspace: a <c>LUNATE_HOME</c> whose store resolves
/// inside that workspace (or the repository it belongs to) is refused with the one message, before
/// the store is opened; a sibling or an unrelated directory is accepted.
/// <para>
/// Each proof runs in a child process because the variable is process-wide: the child is spawned for
/// one case, sets the variable once, and asserts — no other test can observe the mutation.
/// </para>
/// </summary>
public sealed class LunateHomeWorkspaceGuardTests
{
    private const string Refusal =
        "LUNATE_HOME points inside the workspace; trust records there could be written by the repository";

    private const string RootVariable = "LUNATE_HOME";

    [Fact]
    public async Task A_store_inside_the_workspace_is_refused()
    {
        const string Method = nameof(A_store_inside_the_workspace_is_refused);
        if (TestChildProcess.IsChild(Method))
        {
            Probe(
                Method,
                (worktree, _) => Path.Combine(worktree, ".state"),
                (worktree, store) =>
                {
                    (int code, string _, string errors) = CachePrune(worktree);
                    Assert.Equal(1, code);
                    Assert.Contains(Refusal, errors, StringComparison.Ordinal);

                    (int buildCode, _, string buildErrors) = Build(worktree);
                    Assert.Equal(1, buildCode);
                    Assert.Contains(Refusal, buildErrors, StringComparison.Ordinal);

                    using var host = new InteractiveSessionHost(workingDirectory: worktree);
                    host.Console.Complete();
                    host.RunAsync().GetAwaiter().GetResult();
                    Assert.Equal(1, host.Session.ExitCode);
                    Assert.False(host.Session.IsComposed);
                    Assert.Contains(Refusal, host.Diagnostics, StringComparison.Ordinal);

                    // Nothing was created where the refusal pointed: the guard runs before the
                    // store is opened, not after the first write.
                    Assert.False(Directory.Exists(store));
                }
            );
            return;
        }

        await AssertChildPassesAsync(Method);
    }

    [Fact]
    public async Task A_store_equal_to_the_workspace_root_is_refused()
    {
        const string Method = nameof(A_store_equal_to_the_workspace_root_is_refused);
        if (TestChildProcess.IsChild(Method))
        {
            Probe(
                Method,
                (worktree, _) => worktree,
                (worktree, _) =>
                {
                    (int code, _, string errors) = CachePrune(worktree);
                    Assert.Equal(1, code);
                    Assert.Contains(Refusal, errors, StringComparison.Ordinal);
                }
            );
            return;
        }

        await AssertChildPassesAsync(Method);
    }

    [Fact]
    public async Task A_store_in_a_sibling_directory_is_accepted()
    {
        const string Method = nameof(A_store_in_a_sibling_directory_is_accepted);
        if (TestChildProcess.IsChild(Method))
        {
            Probe(
                Method,
                (worktree, temp) => Path.Combine(Path.GetDirectoryName(worktree)!, "state"),
                (worktree, _) =>
                {
                    (int code, string output, string errors) = CachePrune(worktree);
                    Assert.Equal(0, code);
                    Assert.Empty(errors);
                    Assert.Contains("cache:", output, StringComparison.Ordinal);
                }
            );
            return;
        }

        await AssertChildPassesAsync(Method);
    }

    [Fact]
    public async Task A_store_outside_any_workspace_is_accepted()
    {
        const string Method = nameof(A_store_outside_any_workspace_is_accepted);
        if (TestChildProcess.IsChild(Method))
        {
            Probe(
                Method,
                (_, temp) =>
                    Path.Combine(
                        Path.GetTempPath(),
                        string.Concat("lunate-state-", Guid.NewGuid().ToString("N"))
                    ),
                (worktree, _) =>
                {
                    (int code, _, string errors) = CachePrune(worktree);
                    Assert.Equal(0, code);
                    Assert.Empty(errors);
                }
            );
            return;
        }

        await AssertChildPassesAsync(Method);
    }

    /// <summary>
    /// One case: a temp worktree, the case's store, the variable set for this process only, and the
    /// case's own assertions. The variable is cleared afterwards so the process is left as found.
    /// </summary>
    private static void Probe(
        string method,
        Func<string, string, string> store,
        Action<string, string> assert
    )
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string location = store(worktree, temp.Root);

        Environment.SetEnvironmentVariable(RootVariable, location);
        try
        {
            assert(worktree, location);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RootVariable, null);
        }
    }

    private static (int Code, string Output, string Errors) CachePrune(string worktree)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        int code = Cli.Run(
            ["cache", "prune"],
            output: output,
            errors: errors,
            workingDirectory: worktree
        );
        return (code, output.ToString(), errors.ToString());
    }

    private static (int Code, string Output, string Errors) Build(string worktree)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        int code = Cli.Run(
            ["extensions", "build", "acme"],
            output: output,
            errors: errors,
            workingDirectory: worktree
        );
        return (code, output.ToString(), errors.ToString());
    }

    private static async Task AssertChildPassesAsync(string method)
    {
        (int exitCode, string output) = await TestChildProcess.RunAsync(
            typeof(LunateHomeWorkspaceGuardTests),
            method
        );
        Assert.True(
            exitCode == 0,
            $"the child probe failed (exit {exitCode}):{Environment.NewLine}{output}"
        );
    }
}
