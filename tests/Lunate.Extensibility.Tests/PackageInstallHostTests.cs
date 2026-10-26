using Lunate.Extensibility;
using Lunate.Extensibility.Restore;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// A package's content in the host, not only on disk (spec: "Install runs no package code and
/// records no trust", "Project extension trust"): a user-scope install loads with no question, a
/// project install asks the existing question with the package named on one line, the recorded
/// decision binds the package identity, and an install never loads the assembly it wrote.
/// </summary>
public sealed class PackageInstallHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_install_loads_with_no_question_asked()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        TestPackageBundle bundle = HelloBundle(temp.Root);
        bundle.Save();
        var installer = new PackageInstaller(
            new PackageInstallOptions
            {
                Sources = [new SingleFilePackageSource("local", bundle.Feed)],
                GlobalPackagesFolder = bundle.GlobalPackages,
            }
        );
        await installer.InstallAsync(
            new PackageInstallRequest
            {
                Id = "Acme.Toolkit",
                Version = "1.4.2",
                Target = PackageInstallTarget.ForUser(store, "Acme.Toolkit"),
            },
            Ct
        );

        var loader = new ExtensionLoader(new ExtensionHostOptions { StorePath = store });
        loader.Discover(temp.Root, temp.Root);
        var prompt = new RecordingPrompt(decisions: []);
        LoadedExtension loaded = await loader.Load("hello", temp.Root, temp.Root, prompt, Ct);

        Assert.Equal("hello", loaded.Descriptor.Id);
        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task A_project_install_asks_with_the_package_on_one_line_and_binds_it()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(worktree);
        TestPackageBundle bundle = HelloBundle(temp.Root);
        bundle.Save();
        var installer = new PackageInstaller(
            new PackageInstallOptions
            {
                Sources = [new SingleFilePackageSource("local", bundle.Feed)],
                GlobalPackagesFolder = bundle.GlobalPackages,
            }
        );
        await installer.InstallAsync(
            new PackageInstallRequest
            {
                Id = "Acme.Toolkit",
                Version = "1.4.2",
                Target = PackageInstallTarget.ForProject(worktree, store, "Acme.Toolkit"),
            },
            Ct
        );

        var options = new ExtensionHostOptions { StorePath = store };
        var loader = new ExtensionLoader(options);
        loader.Discover(worktree, worktree);
        var prompt = new RecordingPrompt(decisions: [true]);
        await loader.Load("hello", worktree, worktree, prompt, Ct);

        ExtensionTrustRequest request = Assert.Single(prompt.Requests);
        Assert.Equal("from package Acme.Toolkit@1.4.2 (local)", request.FromPackage);

        // The decision binds the package beside the directory content hash: the plain hash is not
        // what was recorded, the bound one is.
        string installed = Path.Combine(worktree, ".lunate", "extensions", "hello");
        string plain = ExtensionTrustStore.ComputeContentHash(installed);
        var store2 = new ExtensionTrustStore(options);
        Assert.Equal(ExtensionTrustDecision.Prompt, store2.Evaluate(worktree, worktree, plain));
        string bound = InstalledPackages.BindTrustDigest(
            plain,
            InstalledPackages.Read(store).ForExtension(installed)
        );
        Assert.Equal(ExtensionTrustDecision.Trusted, store2.Evaluate(worktree, worktree, bound));
    }

    [Fact]
    public async Task An_install_loads_no_assembly_and_runs_no_factory()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(worktree);
        TestPackageBundle bundle = new(temp.Root);
        bundle.Extension(
            "marker-tools",
            TestExtensions.FixtureFile("MarkerExtension", "."),
            "MarkerExtension.dll"
        );
        bundle.Skill("greek").Save();

        var installer = new PackageInstaller(
            new PackageInstallOptions
            {
                Sources = [new SingleFilePackageSource("local", bundle.Feed)],
                GlobalPackagesFolder = bundle.GlobalPackages,
            }
        );
        await installer.InstallAsync(
            new PackageInstallRequest
            {
                Id = "Acme.Toolkit",
                Version = "1.4.2",
                Target = PackageInstallTarget.ForUser(store, "Acme.Toolkit"),
            },
            Ct
        );

        string installed = Path.Combine(store, "extensions", "marker-tools");
        Assert.True(File.Exists(Path.Combine(installed, "MarkerExtension.dll")));
        Assert.False(
            File.Exists(Path.Combine(installed, "factory-ran.marker")),
            "the install ran the bundle's factory"
        );
        Assert.False(
            File.Exists(Path.Combine(installed, "assembly-loaded.marker")),
            "the install loaded the bundle's assembly"
        );

        // The control: the markers are real, so their absence above means the code never ran.
        var loader = new ExtensionLoader(new ExtensionHostOptions { StorePath = store });
        loader.Discover(temp.Root, temp.Root);
        await loader.Load("marker-tools", temp.Root, temp.Root, new RecordingPrompt([]), Ct);
        Assert.True(File.Exists(Path.Combine(installed, "factory-ran.marker")));
        Assert.True(File.Exists(Path.Combine(installed, "assembly-loaded.marker")));
    }

    /// <summary>HelloExtension as a bundle: its built DLLs, its manifest and one skill.</summary>
    private static TestPackageBundle HelloBundle(string root) =>
        new TestPackageBundle(root)
            .Extension(
                "hello",
                TestExtensions.FixtureFile("HelloExtension", "."),
                "HelloExtension.dll"
            )
            .Skill("greek");

    /// <summary>
    /// A prompt that records every question and answers from a fixed queue. An empty queue throws,
    /// so a test that expected no question fails loudly instead of approving implicitly.
    /// </summary>
    private sealed class RecordingPrompt(params bool[] decisions) : IExtensionTrustPrompt
    {
        private readonly Queue<bool> _decisions = new(decisions);

        internal List<ExtensionTrustRequest> Requests { get; } = [];

        internal int Calls => Requests.Count;

        public ValueTask<bool> ApproveAsync(
            ExtensionTrustRequest request,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            Requests.Add(request);
            if (!_decisions.TryDequeue(out bool decision))
            {
                throw new InvalidOperationException(
                    $"no decision queued for extension '{request.Descriptor.Id}'; the load asked a question it should not have asked."
                );
            }

            return ValueTask.FromResult(decision);
        }
    }
}
