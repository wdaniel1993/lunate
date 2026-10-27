namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The assembly's surface (spec: "Public API visibility"). Every type here is internal: the NuGet
/// half of the CLI is an implementation detail behind <c>lunate install</c>, <c>uninstall</c> and
/// the single-file restore, so there is no public surface to track — and none to break by accident.
/// A type that becomes public fails this test until someone decides it is a contract and tracks it.
/// </summary>
public sealed class PublicSurfaceTests
{
    [Fact]
    public void The_assembly_exports_no_types()
    {
        string[] exported =
        [
            .. typeof(PackageInstaller)
                .Assembly.GetExportedTypes()
                .Select(type => type.FullName ?? type.Name),
        ];

        Assert.Empty(exported);
    }

    [Fact]
    public void The_consumer_reaches_the_install_types_through_internals_visible_to()
    {
        // The install, uninstall and restore types are internal to this assembly and reachable
        // because the CLI's assembly is granted access: this test compiles and runs only if that
        // grant holds, which is the other half of the decision above.
        string store = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "store"))
        );

        PackageInstallTarget target = PackageInstallTarget.ForUser(store, "Acme.Toolkit");

        Assert.Equal(PackageScope.User, target.Scope);
        Assert.Equal(Path.Combine(store, "packages", "acme.toolkit"), target.RecordDirectory);
    }
}
