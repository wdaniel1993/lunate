namespace Lunate.Coding.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public void Constructor_rejects_a_missing_working_directory()
    {
        Assert.Throws<ArgumentException>(() => new Workspace(""));
        Assert.Throws<ArgumentException>(() => new Workspace("   "));
    }

    [Fact]
    public void Relative_path_inside_resolves_with_a_forward_slash_display_form()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));
        File.WriteAllText(temp.File("src/File.cs"), "class C;\n");
        var workspace = new Workspace(temp.Root);

        var accepted = workspace.TryResolve("src/File.cs", out var resolved, out var error);

        Assert.True(accepted, error);
        Assert.Equal(Workspace.Canonicalize(temp.File("src/File.cs")), resolved.AbsolutePath);
        Assert.Equal("src/File.cs", resolved.RelativePath);
    }

    [Fact]
    public void Absolute_path_inside_resolves()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.cs"), "class C;\n");
        var workspace = new Workspace(temp.Root);

        var accepted = workspace.TryResolve(temp.File("File.cs"), out var resolved, out var error);

        Assert.True(accepted, error);
        Assert.Equal(Workspace.Canonicalize(temp.File("File.cs")), resolved.AbsolutePath);
        Assert.Equal("File.cs", resolved.RelativePath);
    }

    [Fact]
    public void Lexical_escape_is_refused_naming_target_and_roots()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        Directory.CreateDirectory(work);
        File.WriteAllText(temp.File("outside.txt"), "secret");
        var workspace = new Workspace(work);

        var accepted = workspace.TryResolve("../outside.txt", out _, out var error);

        Assert.False(accepted);
        Assert.Equal(
            $"path '../outside.txt' is outside the workspace "
                + $"(resolves to '{Workspace.Canonicalize(temp.File("outside.txt"))}'; "
                + $"allowed roots: {Workspace.Canonicalize(work)})",
            error
        );
    }

    [Fact]
    public void A_climb_that_stays_inside_is_accepted()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));
        File.WriteAllText(temp.File("File.cs"), "class C;\n");
        var workspace = new Workspace(temp.Root);

        var accepted = workspace.TryResolve("src/../File.cs", out var resolved, out var error);

        Assert.True(accepted, error);
        Assert.Equal("File.cs", resolved.RelativePath);
    }

    [Fact]
    public void Missing_paths_and_empty_paths_are_handled()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);

        Assert.False(workspace.TryResolve("", out _, out var emptyError));
        Assert.Equal("path must not be empty", emptyError);
        Assert.False(workspace.TryResolve("   ", out _, out _));

        var accepted = workspace.TryResolve("does/not/exist.txt", out var resolved, out var error);

        Assert.True(accepted, error);
        Assert.Equal("does/not/exist.txt", resolved.RelativePath);
    }

    [Fact]
    public void A_symlink_pointing_outside_is_refused()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        Directory.CreateDirectory(work);
        var outside = temp.File("outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        if (!TryCreateDirectoryLink(Path.Combine(work, "link"), outside))
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }

        var workspace = new Workspace(work);
        var accepted = workspace.TryResolve("link/secret.txt", out _, out var error);

        Assert.False(accepted);
        Assert.Contains("outside the workspace", error);
    }

    [Fact]
    public void A_symlink_pointing_inside_is_allowed_and_reports_the_target()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        Directory.CreateDirectory(work);
        var real = Path.Combine(work, "real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "file.txt"), "x");
        if (!TryCreateDirectoryLink(Path.Combine(work, "link"), real))
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }

        var workspace = new Workspace(work);
        var accepted = workspace.TryResolve("link/file.txt", out var resolved, out var error);

        Assert.True(accepted, error);
        Assert.Equal(Workspace.Canonicalize(Path.Combine(real, "file.txt")), resolved.AbsolutePath);
        Assert.Equal("real/file.txt", resolved.RelativePath);
    }

    [Fact]
    public void Case_variants_follow_the_file_system()
    {
        using var temp = new TempDirectory();
        var root = temp.File("CaseProbe");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "file.txt"), "x");
        var caseInsensitive = Directory.Exists(temp.File("caseprobe"));
        var workspace = new Workspace(root);

        var exactAccepted = workspace.TryResolve(
            Path.Combine(root, "file.txt"),
            out var exact,
            out var error
        );
        Assert.True(exactAccepted, error);
        Assert.Equal("file.txt", exact.RelativePath);

        var variantAccepted = workspace.TryResolve(temp.File("caseprobe/file.txt"), out _, out _);

        Assert.Equal(caseInsensitive, variantAccepted);
    }

    [Fact]
    public void An_extra_root_widens_the_boundary()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        var extra = temp.File("extra");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(extra);
        File.WriteAllText(Path.Combine(extra, "file.txt"), "x");
        var workspace = new Workspace(work, [extra]);

        var accepted = workspace.TryResolve(
            Path.Combine(extra, "file.txt"),
            out var resolved,
            out var error
        );

        Assert.True(accepted, error);
        Assert.Equal("file.txt", resolved.RelativePath);
    }

    [Fact]
    public void A_dangling_symlink_pointing_outside_is_refused()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        Directory.CreateDirectory(work);
        if (!TryCreateDirectoryLink(Path.Combine(work, "link"), temp.File("outside")))
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }

        var workspace = new Workspace(work);
        var accepted = workspace.TryResolve("link/created-by-escape.txt", out _, out var error);

        Assert.False(accepted);
        Assert.Contains("outside the workspace", error);
        Assert.False(File.Exists(temp.File("outside/created-by-escape.txt")));
    }

    [Fact]
    public void A_symlink_cycle_is_reported_as_a_resolution_error()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        Directory.CreateDirectory(work);
        var first = Path.Combine(work, "a");
        var second = Path.Combine(work, "b");
        if (!TryCreateDirectoryLink(first, second))
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }
        if (!TryCreateDirectoryLink(second, first))
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }

        var workspace = new Workspace(work);
        var accepted = workspace.TryResolve("a", out _, out var error);

        Assert.False(accepted);
        Assert.Contains("could not be resolved", error);
    }

    [Fact]
    public void A_main_checkout_exposes_its_repository_identity()
    {
        using var temp = new TempDirectory();
        var root = temp.File("main");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var workspace = new Workspace(root);

        var canonicalRoot = Workspace.Canonicalize(root);
        Assert.Equal(canonicalRoot, workspace.WorktreeRoot);
        Assert.Equal(canonicalRoot, workspace.RepoRoot);
        Assert.Equal(Workspace.Canonicalize(Path.Combine(root, ".git")), workspace.GitCommonDir);
    }

    [Fact]
    public void A_linked_worktree_reports_the_repository_identity()
    {
        using var temp = new TempDirectory();
        var (main, worktree) = CreateLinkedWorktree(temp, "wt");
        var workspace = new Workspace(worktree);

        Assert.Equal(Workspace.Canonicalize(worktree), workspace.WorktreeRoot);
        Assert.Equal(Workspace.Canonicalize(main), workspace.RepoRoot);
        Assert.Equal(Workspace.Canonicalize(Path.Combine(main, ".git")), workspace.GitCommonDir);
    }

    [Fact]
    public void A_non_repository_workspace_has_no_identity_and_keeps_working()
    {
        using var temp = new TempDirectory();
        var root = temp.File("plain");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "file.txt"), "x");
        var workspace = new Workspace(root);

        Assert.Equal(Workspace.Canonicalize(root), workspace.WorktreeRoot);
        Assert.Null(workspace.RepoRoot);
        Assert.Null(workspace.GitCommonDir);
        Assert.True(workspace.TryResolve("file.txt", out _, out var error), error);
    }

    [Fact]
    public void A_malformed_git_file_yields_no_identity_without_throwing()
    {
        using var temp = new TempDirectory();
        var root = temp.File("broken");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".git"), "not a gitdir pointer\n");
        var workspace = new Workspace(root);

        Assert.Equal(Workspace.Canonicalize(root), workspace.WorktreeRoot);
        Assert.Null(workspace.RepoRoot);
        Assert.Null(workspace.GitCommonDir);
    }

    [Fact]
    public void A_gitdir_without_commondir_yields_no_identity_without_throwing()
    {
        using var temp = new TempDirectory();
        var root = temp.File("broken");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(temp.File("gitdir"));
        File.WriteAllText(Path.Combine(root, ".git"), "gitdir: ../gitdir\n");
        var workspace = new Workspace(root);

        Assert.Null(workspace.RepoRoot);
        Assert.Null(workspace.GitCommonDir);
    }

    [Fact]
    public void A_symlinked_worktree_root_is_canonicalized_for_identity()
    {
        using var temp = new TempDirectory();
        var (main, worktree) = CreateLinkedWorktree(temp, "wt");
        if (!TryCreateDirectoryLink(temp.File("wt-link"), worktree))
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }

        var workspace = new Workspace(temp.File("wt-link"));

        Assert.Equal(Workspace.Canonicalize(worktree), workspace.WorktreeRoot);
        Assert.Equal(Workspace.Canonicalize(main), workspace.RepoRoot);
        Assert.Equal(Workspace.Canonicalize(Path.Combine(main, ".git")), workspace.GitCommonDir);
    }

    private static (string Main, string Worktree) CreateLinkedWorktree(
        TempDirectory temp,
        string name
    )
    {
        var main = temp.File("main");
        var worktree = temp.File(name);
        var gitDir = Path.Combine(main, ".git", "worktrees", name);
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(worktree);
        File.WriteAllText(
            Path.Combine(worktree, ".git"),
            $"gitdir: ../main/.git/worktrees/{name}\n"
        );
        File.WriteAllText(Path.Combine(gitDir, "commondir"), "../..\n");
        return (main, worktree);
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            return false;
        }
    }
}
