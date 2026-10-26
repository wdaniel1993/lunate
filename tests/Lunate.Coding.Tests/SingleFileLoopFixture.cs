using Lunate.Extensibility;
using Lunate.Extensibility.Compile;

namespace Lunate.Coding.Tests;

/// <summary>
/// One single-file extension's world for a session-level test: a temp store and worktree, a
/// packages folder holding the running runtime's reference assemblies and one compiled package,
/// and the restore and compile seams a composed session needs. The package and the extension are
/// compiled by the real compiler against the machine's reference assemblies, so a run in which
/// "the model wrote a file declaring one package" exercises the whole path instead of simulating
/// it. A machine with neither a NuGet cache nor an SDK targeting pack skips the family with the
/// reason, the way the compile suite does, because a compile against a fabricated pack would
/// prove nothing. The pack preparation lives in the package half of this partial.
/// </summary>
internal sealed partial class SingleFileLoopFixture : IDisposable
{
    /// <summary>The id the extension declares; it is also the assembly's simple name.</summary>
    internal const string Id = "acme-tools";

    /// <summary>The one package the file declares, and the code it compiles against.</summary>
    internal const string PackageId = "Acme.Tools";

    internal const string PackageVersion = "2.0.0";

    /// <summary>The package's own code: what proves the compile read the declared package.</summary>
    internal const string PackageSource = """
        namespace Acme.Tools;

        public static class Tools
        {
            public static string Greeting() => "hello from the package";
        }
        """;

    private readonly TempDirectory _temp;
    private readonly ExtensionLockPackage _package;

    private SingleFileLoopFixture(
        TempDirectory temp,
        string packagesFolder,
        string packVersion,
        ExtensionLockReferencePack referencePack
    )
    {
        _temp = temp;
        Packages = packagesFolder;
        ReferencePack = referencePack;
        Target = SingleFileTarget.Current();
        // Canonical roots, like the session's: a trust record is keyed by the file's full path, so
        // a fixture that wrote one under a symlinked spelling (macOS `/var` → `/private/var`) would
        // make the file the session discovers a different file from the one it approved.
        Directory.CreateDirectory(temp.File("store"));
        Directory.CreateDirectory(temp.File("worktree"));
        Store = Workspace.Canonicalize(temp.File("store"));
        Worktree = Workspace.Canonicalize(temp.File("worktree"));
        ExtensionsRoot = Path.Combine(Worktree, ".lunate", "extensions");
        Directory.CreateDirectory(ExtensionsRoot);
        _package = AddPackage(packVersion);
    }

    /// <summary>The <c>~/.lunate</c> stand-in: the trust record, the lock and the build cache.</summary>
    internal string Store { get; }

    /// <summary>The session's working directory, so the file is discovered as a project extension.</summary>
    internal string Worktree { get; }

    internal string ExtensionsRoot { get; }

    /// <summary>Where the locked package and the reference pack are installed, NuGet's layout.</summary>
    internal string Packages { get; }

    internal SingleFileTarget Target { get; }

    internal ExtensionLockReferencePack ReferencePack { get; }

    /// <summary>The file the model writes: <c>&lt;worktree&gt;/.lunate/extensions/acme.cs</c>.</summary>
    internal string SourcePath => Path.Combine(ExtensionsRoot, "acme.cs");

    /// <summary>How often a restore engine was asked; zero is how "nothing reached a feed" reads.</summary>
    internal int Restores { get; private set; }

    /// <summary>
    /// How often the compile factory was called. The builder asks it only after trust and the
    /// cache answered, so an unchanged count is "the changed file was not compiled".
    /// </summary>
    internal int Compiles { get; private set; }

    /// <summary>
    /// The seams a session composes with: the temp store, the temp packages folder (so a load and
    /// its compile agree and neither reads the user's NuGet cache), the fixture engine and the
    /// real compiler behind counted factories.
    /// </summary>
    internal ExtensionSeams Seams(
        IExtensionTrustPrompt? prompt = null,
        RecordingExtensionLog? log = null,
        Action<string>? notice = null
    ) =>
        new()
        {
            StorePath = Store,
            GlobalPackagesFolder = Packages,
            Log = log ?? new RecordingExtensionLog(),
            Prompt = prompt,
            Notice = notice,
            RestoreEngine = () =>
            {
                Restores++;
                return new FixtureEngine(this);
            },
            Compiler = () =>
            {
                Compiles++;
                return new RoslynSingleFileCompiler();
            },
        };

    /// <summary>
    /// The source the model writes: one tool, one declared package, and a probe the tool answers
    /// with so a test can tell one build from the next. The literal is inserted verbatim.
    /// </summary>
    internal static string Source(string probeLiteral) =>
        Template.Replace("__PROBE__", probeLiteral, StringComparison.Ordinal);

    /// <summary>Writes the file without recording a decision: the untrusted case.</summary>
    internal string Write(string source)
    {
        Directory.CreateDirectory(ExtensionsRoot);
        File.WriteAllText(SourcePath, source);
        return SourcePath;
    }

    /// <summary>Writes the file and records the first-trust decision, the way an approving session does.</summary>
    internal string WriteTrusted(string source)
    {
        Directory.CreateDirectory(ExtensionsRoot);
        File.WriteAllText(SourcePath, source);
        ExtensionHeader header = ExtensionHeaderScanner.Scan(source);
        Assert.True(header.IsValid, "the fixture's header must be valid");
        new ExtensionTrustStore(
            new ExtensionHostOptions { StorePath = Store }
        ).RecordSingleFileTrust(
            SourcePath,
            SingleFileExtensionDigest.ComputeUnrestored(
                header.Id!,
                header.Packages,
                File.ReadAllBytes(SourcePath)
            )
        );
        return SourcePath;
    }

    /// <summary>
    /// Writes, trusts and builds one source, the way an approving session would, so a session can
    /// start with a file that is already `built` and load it without a question. The counters are
    /// cleared afterwards: they measure what the session under test asked for, not what the
    /// fixture prepared.
    /// </summary>
    internal async Task<SingleFileBuildOutcome> BuildAsync(string source)
    {
        WriteTrusted(source);
        SingleFileBuildOutcome outcome = await Builder()
            .BuildAsync(SourcePath, ExtensionScope.Project, prompt: null, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.True(
            outcome.Succeeded,
            FormattableString.Invariant(
                $"the fixture build failed: {outcome.Status} — {outcome.Reason}"
            )
        );
        Restores = 0;
        Compiles = 0;
        return outcome;
    }

    /// <summary>The build cache over this fixture's store (what <c>builds.json</c> says is current).</summary>
    internal SingleFileBuildCache BuildCache() => new(Store);

    /// <summary>The build service both the CLI and a reload use, over this fixture's seams.</summary>
    internal SingleFileExtensionBuilder Builder() =>
        new(
            new ExtensionHostOptions { StorePath = Store, GlobalPackagesFolder = Packages },
            () => new FixtureEngine(this),
            () => new RoslynSingleFileCompiler(),
            Target,
            Packages
        );

    public void Dispose() => _temp.Dispose();

    private const string Template = """
        #:extension acme-tools@1.0.0
        #:tool whoami
        #:package Acme.Tools@2.0.0

        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using Lunate.Agent;
        using Lunate.Extensibility.Abstractions;

        public sealed class AcmeExtension : IExtensionFactory
        {
            public IExtension Create(IExtensionContext context)
            {
                context.RegisterTool(
                    new ExtensionToolRegistration
                    {
                        Name = "whoami",
                        Description = "answers with the package's greeting and this build's probe",
                        ParametersSchema = JsonDocument.Parse("{}").RootElement.Clone(),
                        // It only reads its build probe, so the fixture declares it honestly:
                        // the reload/trust tests are not about the approval panel.
                        Annotations = new ToolAnnotations(ReadOnly: true),
                        Handler = new WhoAmI(),
                    }
                );
                return new Shell();
            }
        }

        internal sealed class WhoAmI : IExtensionToolHandler
        {
            public Task<ToolResult> InvokeAsync(
                JsonElement arguments,
                ToolContext context,
                CancellationToken ct
            ) => Task.FromResult(new ToolResult(Probe.Value, false));
        }

        internal sealed class Shell : IExtension;

        internal static class Probe
        {
            public static string Value =>
                global::Acme.Tools.Tools.Greeting() + ": " + __PROBE__;
        }
        """;
}
