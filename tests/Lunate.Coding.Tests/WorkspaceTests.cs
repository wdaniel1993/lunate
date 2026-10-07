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
