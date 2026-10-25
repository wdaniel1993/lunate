using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The two surfaces this phase adds to the CLI: <c>lunate extensions build</c> — the headless compile
/// trigger, which must run the same service a reload does and must never approve trust — and
/// <c>lunate cache prune</c>, which removes what no discovered file points at. Both are wired through
/// <see cref="Cli.Run"/> with a temp store, so what is exercised is the real command routing.
/// </summary>
public sealed class ExtensionBuildCommandTests
{
    private const string Source = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue

        public sealed class AcmeExtension : Lunate.Extensibility.Abstractions.IExtensionFactory
        {
            public Lunate.Extensibility.Abstractions.IExtension Create(
                Lunate.Extensibility.Abstractions.IExtensionContext context) => new Shell();
        }

        internal sealed class Shell : Lunate.Extensibility.Abstractions.IExtension { }
        """;

    [Fact]
    public void An_untrusted_file_reports_the_trust_step_and_is_never_approved_by_the_cli()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        string file = WriteFile(store);
        var compiler = new CountingCompiler();
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exit = Cli.Run(
            ["extensions", "build", "acme-tools"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams
            {
                StorePath = store,
                Compiler = compiler.Create,
                RestoreEngine = () =>
                    throw new InvalidOperationException(
                        "an untrusted file must not reach a restore engine"
                    ),
            }
        );

        Assert.Equal(ExtensionBuildCommand.ExitFailure, exit);
        Assert.Equal(0, compiler.Creations);
        Assert.Contains("failed (trust)", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "run an interactive session to approve it",
            errors.ToString(),
            StringComparison.Ordinal
        );
        // Nothing was written: no lock, no build.
        Assert.False(Directory.Exists(SingleFileExtensionPaths.EntryPath(store, "acme-tools")));
    }

    [Fact]
    public void A_json_failure_names_the_step_and_keeps_stdout_clean()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        string file = WriteFile(
            store,
            Source.Replace("new Shell()", "new Shell(1)", StringComparison.Ordinal)
        );

        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exit = Cli.Run(
            ["extensions", "build", "acme-tools", "--json"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams
            {
                StorePath = store,
                Compiler = new CountingCompiler().Create,
            }
        );

        // The file is not trusted, so the first step that fails is trust — the compile is never
        // reached, and the JSON says which step and why.
        Assert.Equal(ExtensionBuildCommand.ExitFailure, exit);
        Assert.Contains("\"failure\":\"trust\"", errors.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Cache_prune_removes_an_entry_no_discovered_file_declares()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        string live = WriteFile(store);
        string orphan = SingleFileExtensionPaths.EntryPath(store, "gone-tools");
        Directory.CreateDirectory(Path.Combine(orphan, "builds", "0123456789abcdef"));
        File.WriteAllText(Path.Combine(orphan, "extension.lock.json"), "{}");

        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exit = Cli.Run(
            ["cache", "prune"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: store
        );

        Assert.Equal(CacheCommand.ExitOk, exit);
        Assert.Contains("removed 'gone-tools'", output.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(orphan));
        Assert.True(File.Exists(live));
    }

    [Fact]
    public void Cache_prune_with_nothing_to_do_says_so_and_removes_nothing()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        string live = WriteFile(store);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exit = Cli.Run(
            ["cache", "prune", "--json"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: store
        );

        Assert.Equal(CacheCommand.ExitOk, exit);
        Assert.Equal("{\"removed\":[]}", output.ToString().Trim());
        Assert.True(File.Exists(live));
    }

    /// <summary>
    /// The file where a user extension lives: <c>&lt;store&gt;/extensions/acme.cs</c>, which is the
    /// global root discovery reads.
    /// </summary>
    private static string WriteFile(string store, string? source = null)
    {
        string directory = Path.Combine(store, "extensions");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "acme.cs");
        File.WriteAllText(path, source ?? Source);
        return path;
    }

    /// <summary>The compile factory as the CLI passes it: created only when a compile will run.</summary>
    private sealed class CountingCompiler
    {
        internal int Creations { get; private set; }

        internal ISingleFileCompiler? Create()
        {
            Creations++;
            return new Lunate.Extensibility.Compile.RoslynSingleFileCompiler();
        }
    }
}
