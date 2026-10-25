using System.Diagnostics;
using System.Globalization;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The lazy-loading contract (spec: "Restore, lock and compiled cache" — "No NuGet assembly before a
/// restore"): a session whose extensions are all already built composes without loading a single
/// <c>NuGet.*</c> assembly. It runs in a child process because the rest of this suite restores for
/// real, and a loaded assembly cannot be unloaded — the assertion would otherwise depend on test
/// order.
/// </summary>
public sealed class SingleFileLazyRestoreTests
{
    private const string ProbeVariable = "LUNATE_NUGET_LAZY_PROBE";
    private const string MarkerVariable = "LUNATE_NUGET_LAZY_PROBE_MARKER";

    [Fact]
    public async Task An_all_built_session_composes_without_loading_a_nuget_assembly()
    {
        string? tool = Environment.GetEnvironmentVariable(ProbeVariable);
        if (tool == "1")
        {
            await ProbeAsync();
            File.WriteAllText(Environment.GetEnvironmentVariable(MarkerVariable)!, "probe ran");
            return;
        }

        string marker = Path.Combine(
            Path.GetTempPath(),
            string.Concat("lunate-nuget-probe-", Guid.NewGuid().ToString("N"), ".marker")
        );
        try
        {
            using Process child = LaunchProbe(marker);
            string output = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
            Assert.True(
                child.WaitForExit(TimeSpan.FromSeconds(180)),
                "the probe process timed out:\n" + output
            );
            Assert.True(
                child.ExitCode == 0,
                $"the probe failed (exit {child.ExitCode}):\n{output}"
            );
            Assert.True(
                File.Exists(marker),
                "the probe test never ran in the child process:\n" + output
            );
        }
        finally
        {
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
    }

    /// <summary>
    /// Composes a session with one built packaged extension and one restored single-file extension,
    /// then asserts the process has no <c>NuGet.*</c> assembly and never loaded the restore seam.
    /// </summary>
    private static async Task ProbeAsync()
    {
        Assert.Empty(NuGetAssemblies());

        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string store = temp.File("store");

        // A packaged extension in the global store: nothing to trust, nothing to build.
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));

        // A trusted single-file extension with a lock that answers for its bytes — the state a
        // restore leaves behind, written here through the public API so preparing it loads nothing.
        string file = Path.Combine(store, "extensions", "acme.cs");
        string source = """
            #:extension acme-tools@1.0.0
            #:tool fetch_issue
            #:package Octokit@14.0.0

            public static class Acme
            {
                public static void Register() { }
            }

            """;
        File.WriteAllText(file, source);
        ExtensionPackageReference octokit = new("Octokit", "14.0.0", "Octokit@14.0.0");
        byte[] bytes = File.ReadAllBytes(file);
        ExtensionLock restored = new()
        {
            ExtensionId = "acme-tools",
            ExtensionVersion = "1.0.0",
            SourceDigest = SingleFileExtensionDigest.ComputeSource("acme-tools", [octokit], bytes),
            PackagesWritten = ["Octokit@14.0.0"],
            Toolset = new ExtensionLockToolset
            {
                Tfm = "net10.0",
                Rid = "osx-arm64",
                Runtime = "10.0.13",
                ReferencePack = new ExtensionLockReferencePack
                {
                    Id = "Microsoft.NETCore.App.Ref",
                    Version = "10.0.13",
                    Rule = ReferencePackRules.Exact,
                    Sha512 = "cmVmLXBhY2s=",
                    Assets = ["ref/net10.0/System.Runtime.dll"],
                },
            },
            Sources = ["nuget.org"],
            Packages = [FakeLockedPackage()],
        };
        string lockPath = SingleFileExtensionPaths.LockPath(store, "acme-tools");
        ExtensionLockJson.WriteTo(lockPath, restored);
        new ExtensionTrustStore(
            new ExtensionHostOptions { StorePath = store }
        ).RecordSingleFileTrust(
            file,
            SingleFileExtensionDigest.Compute(
                "acme-tools",
                [octokit],
                bytes,
                ExtensionLockJson.GraphBytes(restored)
            )
        );

        var workspace = new WorkspaceInfo(Workspace.Canonicalize(worktree), null, null);
        await using ExtensionSession session = await ExtensionSession.StartAsync(
            workspace,
            new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
                Prompt = new NoPromptExpected(),
            }
        );

        Assert.Single(session.Descriptors);
        SingleFileExtension candidate = Assert.Single(session.SingleFileExtensions);
        Assert.Equal("acme-tools", candidate.Id);
        Assert.Equal(SingleFileExtensionState.NotBuilt, candidate.State);
        Assert.NotNull(candidate.Lock);

        string[] loaded = NuGetAssemblies();
        Assert.True(
            loaded.Length == 0,
            "a session whose extensions are all built loaded NuGet: " + string.Join(", ", loaded)
        );
        Assert.DoesNotContain(
            "Lunate.Extensibility.Restore",
            LoadedAssemblyNames(),
            StringComparer.Ordinal
        );
    }

    private static ExtensionLockPackage FakeLockedPackage() =>
        new()
        {
            Id = "Octokit",
            Version = "14.0.0",
            Sha512 = "ZmFrZQ==",
            Requested = "Octokit@14.0.0",
            Direct = true,
            Files = ["lib/net10.0/Octokit.dll", "Octokit.nuspec"],
            Compile = ["lib/net10.0/Octokit.dll"],
            Runtime = ["lib/net10.0/Octokit.dll"],
        };

    private static string[] NuGetAssemblies() =>
        [
            .. LoadedAssemblyNames()
                .Where(name => name.StartsWith("NuGet.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

    private static string[] LoadedAssemblyNames() =>
        [
            .. AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetName().Name ?? string.Empty),
        ];

    private static Process LaunchProbe(string markerPath)
    {
        string assembly = typeof(SingleFileLazyRestoreTests).Assembly.Location;
        string method = string.Concat(
            typeof(SingleFileLazyRestoreTests).FullName,
            ".",
            nameof(An_all_built_session_composes_without_loading_a_nuget_assembly)
        );

        string? processPath = Environment.ProcessPath;
        bool direct =
            processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "Lunate.Coding.Tests",
                StringComparison.Ordinal
            );

        var startInfo = new ProcessStartInfo(direct ? processPath! : DotNetPath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!direct)
        {
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(assembly);
        }

        startInfo.ArgumentList.Add("-method");
        startInfo.ArgumentList.Add(method);
        startInfo.ArgumentList.Add("-noLogo");
        startInfo.Environment[ProbeVariable] = "1";
        startInfo.Environment[MarkerVariable] = markerPath;
        return Process.Start(startInfo)!;
    }

    private static string DotNetPath()
    {
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }

    /// <summary>A prompt that fails the test if composition asks anything: nothing here needs trust.</summary>
    private sealed class NoPromptExpected : IExtensionTrustPrompt
    {
        public ValueTask<bool> ApproveAsync(
            ExtensionTrustRequest request,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"composing an all-built session asked to trust '{request.Descriptor.Id}'"
                )
            );

        public ValueTask<bool> ApproveSingleFileAsync(
            SingleFileTrustRequest request,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"composing an all-built session asked to trust single file '{request.Id}'"
                )
            );
    }
}
