using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>A log that keeps every line, so a test can assert how often something was said.</summary>
internal sealed class RecordingLog : IExtensionLog
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];

    internal IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    public void Info(string message) => Add($"info: {message}");

    public void Warn(string message) => Add($"warn: {message}");

    public void Error(string message) => Add($"error: {message}");

    /// <summary>The lines containing <paramref name="text"/> (ordinal).</summary>
    internal IReadOnlyList<string> Matching(string text) =>
        [.. Messages.Where(message => message.Contains(text, StringComparison.Ordinal))];

    internal bool Contains(string text) => Matching(text).Count > 0;

    internal int Count(string text) => Matching(text).Count;

    private void Add(string message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
    }
}

/// <summary>
/// A restore engine a test can hold inside: <see cref="Hold"/> makes the next restore wait after it
/// has been asked, so a reload can be parked in its build phase while the test drives the running
/// extension. It answers with whatever packages the test put in <see cref="Packages"/> — the same
/// shape the fixture's non-gated engine uses — and never touches a feed.
/// </summary>
internal sealed class GatedRestoreEngine(CompileFixture fixture) : ISingleFileRestoreEngine
{
    private readonly SemaphoreSlim _entered = new(0);
    private readonly ManualResetEventSlim _gate = new(true);

    internal List<ExtensionLockPackage> Packages { get; } = [];

    /// <summary>How many restores were asked for (a preflight that stops early leaves this at 0).</summary>
    internal int Requests { get; private set; }

    internal void Hold() => _gate.Reset();

    internal void Release() => _gate.Set();

    /// <summary>Completes when a restore has been asked for and is waiting on the gate.</summary>
    internal Task WaitEnteredAsync(TimeSpan timeout) => _entered.WaitAsync(timeout);

    public Task<SingleFileRestoreResult> RestoreAsync(
        SingleFileRestoreRequest request,
        CancellationToken cancellationToken
    )
    {
        Requests++;
        _entered.Release();
        if (!_gate.Wait(TimeSpan.FromSeconds(60), cancellationToken))
        {
            throw new TimeoutException("the test never released the gated restore");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new SingleFileRestoreResult
            {
                Packages = [.. Packages],
                ReferencePack = fixture.ReferencePack,
                Sources = ["local"],
                GlobalPackagesFolder = fixture.PackagesFolder,
            }
        );
    }
}

/// <summary>Refuses to answer: a load must never ask a trust question of its own.</summary>
internal sealed class NeverPrompt : IExtensionTrustPrompt
{
    public ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("a load must not ask the packaged trust question");

    public ValueTask<bool> ApproveSingleFileAsync(
        SingleFileTrustRequest request,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("a load must not ask the single-file trust question");
}

/// <summary>
/// One trusted, built single-file extension in a temp store, plus the loader that loads it: the
/// phase-3 seam. The store is the global extensions root (so the file is discovered as a user
/// extension) and the packages folder is the compile fixture's, so a package's real assembly can be
/// placed where a build's runtime assets point.
/// </summary>
internal sealed class SingleFileLoadFixture : IDisposable
{
    /// <summary>The package every generated source references; the compile needs a real one.</summary>
    internal const string PackageId = "Acme.Tools";

    internal const string PackageVersion = "2.0.0";

    private readonly CompileFixture _compile;

    private SingleFileLoadFixture(CompileFixture compile)
    {
        _compile = compile;
        Engine = new GatedRestoreEngine(compile);
    }

    internal static SingleFileLoadFixture Create() => new(CompileFixture.Create());

    internal CompileFixture Compile => _compile;

    internal GatedRestoreEngine Engine { get; }

    /// <summary>The compile factory, counted: a preflight that stops early must not reach it.</summary>
    internal CountingCompilerFactory Compiler { get; } = new();

    internal RecordingLog Log { get; } = new();

    /// <summary>The one-line notices the host emitted (a cancelled invocation, in phase 3).</summary>
    internal List<string> Notices { get; } = [];

    internal string Store => _compile.Store;

    internal string ExtensionsRoot => _compile.Extensions;

    internal string Id => "acme-tools";

    internal string SourcePath => Path.Combine(ExtensionsRoot, "acme.cs");

    /// <summary>Adds the package every generated source references, compiled by the real compiler.</summary>
    internal ExtensionLockPackage AddToolPackage(string code, CancellationToken cancellationToken)
    {
        ExtensionLockPackage package = _compile.AddPackage(
            PackageId,
            PackageVersion,
            code,
            cancellationToken
        );
        Engine.Packages.Add(package);
        return package;
    }

    /// <summary>
    /// A package whose <c>lib/</c> carries a copy of the host's own contract assembly: what a real
    /// package that ships its dependencies looks like, and the bytes a load must refuse to bind to.
    /// </summary>
    internal ExtensionLockPackage AddContractCopy(string id, string version)
    {
        string asset = FormattableString.Invariant(
            $"lib/{_compile.Target.Tfm}/Lunate.Extensibility.Abstractions.dll"
        );
        string directory = Path.Combine(
            _compile.PackagesFolder,
            id.ToLowerInvariant(),
            version.ToLowerInvariant(),
            "lib",
            _compile.Target.Tfm
        );
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Lunate.Extensibility.Abstractions.dll");
        File.Copy(typeof(IExtensionFactory).Assembly.Location, path, overwrite: true);
        byte[] bytes = File.ReadAllBytes(path);

        var package = new ExtensionLockPackage
        {
            Id = id,
            Version = version,
            Sha512 = Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(bytes)),
            Requested = FormattableString.Invariant($"{id}@{version}"),
            Direct = true,
            Files = [asset],
            Compile = [],
            Runtime = [asset],
            Native = [],
            Analyzers = [],
            Generators = false,
        };
        Engine.Packages.Add(package);
        return package;
    }

    /// <summary>Writes the source and records the first-trust decision, the way an approving session does.</summary>
    internal string Write(string source)
    {
        Directory.CreateDirectory(ExtensionsRoot);
        File.WriteAllText(SourcePath, source);
        ExtensionHeader header = ExtensionHeaderScanner.Scan(source);
        Assert.True(header.IsValid, "the fixture's header must be valid");
        new ExtensionTrustStore(_compile.Options()).RecordSingleFileTrust(
            SourcePath,
            SingleFileExtensionDigest.ComputeUnrestored(
                header.Id!,
                header.Packages,
                File.ReadAllBytes(SourcePath)
            )
        );
        return SourcePath;
    }

    internal SingleFileExtensionBuilder Builder() =>
        new(
            _compile.Options(),
            () => Engine,
            Compiler.Create,
            _compile.Target,
            _compile.PackagesFolder
        );

    /// <summary>Writes, trusts and builds one source; asserts the build happened.</summary>
    internal async Task<SingleFileBuildOutcome> BuildAsync(
        string source,
        IExtensionTrustPrompt? prompt = null,
        CancellationToken cancellationToken = default
    )
    {
        Write(source);
        SingleFileBuildOutcome outcome = await Builder()
            .BuildAsync(SourcePath, ExtensionScope.Global, prompt, cancellationToken)
            .ConfigureAwait(false);
        Assert.True(
            outcome.Succeeded,
            FormattableString.Invariant(
                $"the fixture build failed: {outcome.Status} — {outcome.Reason}"
            )
        );
        return outcome;
    }

    /// <summary>
    /// Rewrites the apiVersion one build recorded, the way a cache written by another host (or a
    /// hand-edited record) would read: the directory and its key stay, the version the compile saw
    /// changes. Only that field moves, so the test is about the record and nothing else.
    /// </summary>
    internal void RewriteBuildApiVersion(string key, string apiVersion)
    {
        string path = SingleFileExtensionPaths.BuildManifestPath(Store, Id, key);
        System.Text.Json.Nodes.JsonNode document = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(path)
        )!;
        document["apiVersion"] = apiVersion;
        File.WriteAllText(
            path,
            document.ToJsonString(
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            ) + "\n"
        );
    }

    /// <summary>The build cache over this fixture's store (what <c>builds.json</c> says is current).</summary>
    internal SingleFileBuildCache BuildCache() => new(Store);

    internal ExtensionLoader Loader(HookRunnerOptions? hookOptions = null) =>
        new(
            new ExtensionHostOptions
            {
                StorePath = Store,
                Log = Log,
                Notify = Notices.Add,
                GlobalPackagesFolder = _compile.PackagesFolder,
            },
            hookOptions is null ? null : new HookRunner(hookOptions, Log)
        );

    /// <summary>A loader that has discovered this fixture's file, as a session does at startup.</summary>
    internal ExtensionLoader Discovered(HookRunnerOptions? hookOptions = null)
    {
        ExtensionLoader loader = Loader(hookOptions);
        loader.Discover(Store, "repo");
        return loader;
    }

    /// <summary>The source every test starts from: one tool, one command, one package.</summary>
    /// <param name="probe">
    /// The C# expression the tool and the command return, so a test can make the answer carry what
    /// it needs (a build marker, the contract's location). The string literal is inserted verbatim.
    /// </param>
    /// <param name="commandBody">The command handler's body; the default answers with the probe.</param>
    internal static string Source(string probe, string? commandBody = null)
    {
        string body = commandBody ?? "return CommandResult.Notice(Probe.Value);";
        return Template
            .Replace("__PROBE__", probe, StringComparison.Ordinal)
            .Replace("__COMMAND_BODY__", body, StringComparison.Ordinal);
    }

    /// <summary>The library the fixture's package ships, so the source has something to call.</summary>
    internal const string Lib = """
        namespace Acme.Tools;

        public static class Tools
        {
            public static string Hello() => "hello";
        }
        """;

    private const string Template = """
        #:extension acme-tools@1.0.0
        #:tool whoami
        #:command ping
        #:package Acme.Tools@2.0.0

        using System;
        using System.Collections.Generic;
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
                        Description = "reports what the build was made of",
                        ParametersSchema = JsonDocument.Parse("{}").RootElement.Clone(),
                        Handler = new WhoAmI(),
                    }
                );
                context.RegisterCommandSource(new Ping(context.Log));
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

        internal sealed class Ping(IExtensionLog log) : ICommandSource
        {
            private static readonly IReadOnlyList<CommandDescriptor> Descriptors =
            [
                new CommandDescriptor("ping", "pings the build", null, "acme-tools", CommandKind.Extension),
            ];

            public string SourceId => "acme-tools";

            public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
                CancellationToken cancellationToken
            ) => ValueTask.FromResult(Descriptors);

            public async ValueTask<CommandResult> InvokeAsync(
                CommandInvocation invocation,
                CancellationToken cancellationToken
            )
            {
                log.Info("ping invoked");
                __COMMAND_BODY__
            }
        }

        internal sealed class Shell : IExtension;

        internal static class Probe
        {
            public static string Value => __PROBE__;
        }
        """;

    public void Dispose() => _compile.Dispose();
}
