using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Restore;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Task 3.1 for the packaged runtime kind: an extension directory that carries a copy of a
/// host-owned assembly loads against the host's copy (the contract types keep one identity) and the
/// substitution is reported once, naming the copy. The single-file half of the same rule — a locked
/// package that ships the contract — is held by <c>SingleFileIsolationTests</c>, and the installed
/// package's half is here: a bundle that ships one still installs, and its load binds the host's.
/// </summary>
public sealed class ExtensionIsolationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_packaged_extension_that_ships_the_contract_loads_and_warns_once()
    {
        using var temp = new TempDirectory();
        string directory = TestExtensions.InstallHelloExtension(temp, "hello");
        File.Copy(
            typeof(IExtensionFactory).Assembly.Location,
            Path.Combine(directory, "Lunate.Extensibility.Abstractions.dll")
        );
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        LoadedExtension loaded = await loader.Load(
            "hello",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: true),
            Ct
        );

        // The factory was found through the host's IExtensionFactory, which is the identity proof:
        // a copy loaded beside it would have implemented a different interface. The instance is
        // held to the same rule explicitly.
        Assert.NotNull(loaded.Extension);
        Assert.Contains(typeof(IExtension), loaded.Extension.GetType().GetInterfaces());

        string warning = Assert.Single(
            log.Messages,
            message =>
                message.Contains(
                    "ignored the copy of Lunate.Extensibility.Abstractions",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains("hello", warning, StringComparison.Ordinal);
        Assert.Contains(directory, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_installed_package_that_ships_the_contract_still_installs_and_loads_the_hosts_copy()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        string built = temp.Subdirectory("built");
        TestExtensions.CopyFixture(built, "HelloExtension.dll", "HelloExtension.Support.dll");
        TestExtensions.CopyHostAssembly(built, "Lunate.Extensibility.Abstractions.dll");
        TestPackageBundle bundle = new TestPackageBundle(temp.Root)
            .Extension("hello", built, "HelloExtension.dll")
            .Skill("greek");
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

        // The package's copy is content — install refuses nothing, and a user-scope install asks
        // nothing — but the load binds the host's copy, which is what the warning reports.
        string installed = Path.Combine(store, "extensions", "hello");
        Assert.True(
            File.Exists(Path.Combine(installed, "Lunate.Extensibility.Abstractions.dll")),
            "the bundle shipped a copy of the contract and the install wrote it"
        );

        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(store, log));
        loader.Discover(temp.Root, temp.Root);
        var prompt = new FakeExtensionTrustPrompt(approve: true);
        LoadedExtension loaded = await loader.Load("hello", temp.Root, temp.Root, prompt, Ct);

        Assert.Equal(0, prompt.Calls);
        Assert.Contains(typeof(IExtension), loaded.Extension.GetType().GetInterfaces());
        string warning = Assert.Single(
            log.Messages,
            message =>
                message.Contains(
                    "ignored the copy of Lunate.Extensibility.Abstractions",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(installed, warning, StringComparison.Ordinal);
    }
}
