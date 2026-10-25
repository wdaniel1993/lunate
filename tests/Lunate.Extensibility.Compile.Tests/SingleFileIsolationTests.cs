using System.Runtime.Loader;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 3.1: a package that ships its own <c>Lunate.Extensibility.Abstractions.dll</c> is accepted at
/// install and ignored at load. The contract the host owns is the one the extension binds to, so
/// type identity survives the compile/load boundary, and the substitution is reported once in one
/// line that names the package rather than leaving the user to wonder which copy is live.
/// </summary>
public sealed class SingleFileIsolationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_packages_that_ship_the_contract_load_against_the_hosts_copy_and_warn_once()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        fixture.AddToolPackage(SingleFileLoadFixture.Lib, Ct);
        fixture.AddContractCopy("Acme.Contracts", "1.0.0");
        fixture.AddContractCopy("Acme.Extra", "1.0.0");
        await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "typeof(IExtensionFactory).Assembly.Location"),
            cancellationToken: Ct
        );

        ExtensionLoader loader = fixture.Discovered();
        LoadedExtension loaded = await loader.LoadSingleFileAsync(fixture.Id, Ct);

        // The extension's own view of the contract is the host's assembly file, not either package's
        // copy — which is what a second identity would have produced.
        CommandResult result = await CommandAsync(loader, "ping");
        Assert.Equal(typeof(IExtensionFactory).Assembly.Location, result.Message);

        // And the copies were never loaded beside it: the extension's context holds no second
        // Lunate.Extensibility.Abstractions.
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(
            loaded.Extension.GetType().Assembly
        )!;
        Assert.NotSame(AssemblyLoadContext.Default, context);
        Assert.DoesNotContain(
            context.Assemblies,
            assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    typeof(IExtensionFactory).Assembly.GetName().Name,
                    StringComparison.Ordinal
                )
        );

        // One line for both packages: the user is told once which copies were dropped and whose
        // copy is live, not once per package per load attempt.
        string warning = Assert.Single(
            fixture.Log.Matching("ignored the copy of Lunate.Extensibility.Abstractions shipped by")
        );
        Assert.Contains("Acme.Contracts", warning, StringComparison.Ordinal);
        Assert.Contains("Acme.Extra", warning, StringComparison.Ordinal);
        Assert.Contains(fixture.Id, warning, StringComparison.Ordinal);
    }

    private static async Task<CommandResult> CommandAsync(ExtensionLoader loader, string name)
    {
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;
        return await source
            .InvokeAsync(new CommandInvocation(name, string.Empty), Ct)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }
}
