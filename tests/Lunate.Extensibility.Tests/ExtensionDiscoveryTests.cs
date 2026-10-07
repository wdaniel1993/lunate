using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionDiscoveryTests
{
    [Fact]
    public void Reads_global_and_project_manifests_without_loading_assemblies()
    {
        using var temp = new TempDirectory();
        string global = TestExtensions.GlobalExtensionDirectory(temp, "alpha");
        string project = TestExtensions.ProjectExtensionDirectory(temp.Root, "beta");
        TestExtensions.WriteManifest(global, "alpha", entryAssembly: "Missing.dll");
        TestExtensions.WriteManifest(project, "beta");

        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        Assert.Empty(loader.DiscoveryErrors);
        Assert.Equal(["alpha", "beta"], descriptors.Select(descriptor => descriptor.Id));
        Assert.Equal(ExtensionScope.Global, descriptors[0].Scope);
        Assert.Equal(ExtensionScope.Project, descriptors[1].Scope);
        Assert.Equal(global, descriptors[0].Directory);
        Assert.Equal("alpha", descriptors[0].Manifest.Id);
    }

    [Fact]
    public void Missing_extension_directories_are_fine()
    {
        using var temp = new TempDirectory();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        Assert.Empty(descriptors);
        Assert.Empty(loader.DiscoveryErrors);
    }

    [Fact]
    public void Invalid_manifests_are_collected_and_never_fatal()
    {
        using var temp = new TempDirectory();
        string broken = TestExtensions.GlobalExtensionDirectory(temp, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "extension.json"), "{ not json");
        string valid = TestExtensions.GlobalExtensionDirectory(temp, "valid");
        TestExtensions.WriteManifest(valid, "valid");

        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        Assert.Equal(["valid"], descriptors.Select(descriptor => descriptor.Id));
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains(Path.Combine(broken, "extension.json"), error.Path);
        Assert.Contains("extension.json", error.Message);
    }

    [Fact]
    public void Missing_manifest_in_an_extension_directory_is_a_discovery_error()
    {
        using var temp = new TempDirectory();
        string empty = TestExtensions.GlobalExtensionDirectory(temp, "empty");
        Directory.CreateDirectory(empty);

        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));

        Assert.Empty(loader.Discover(temp.Root, "repo"));
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains(empty, error.Message);
    }

    [Fact]
    public void Duplicate_ids_across_scopes_name_both_paths()
    {
        using var temp = new TempDirectory();
        string global = TestExtensions.GlobalExtensionDirectory(temp, "hello");
        string project = TestExtensions.ProjectExtensionDirectory(temp.Root, "hello");
        TestExtensions.WriteManifest(global, "hello");
        TestExtensions.WriteManifest(project, "hello");

        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        Assert.Equal(["hello"], descriptors.Select(descriptor => descriptor.Id));
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains(global, error.Message);
        Assert.Contains(project, error.Message);
    }

    [Fact]
    public void Incompatible_api_versions_are_refused_naming_extension_range_and_current()
    {
        using var temp = new TempDirectory();
        string directory = TestExtensions.GlobalExtensionDirectory(temp, "future");
        TestExtensions.WriteManifest(directory, "future", apiVersion: "2.0.0");

        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));

        Assert.Empty(loader.Discover(temp.Root, "repo"));
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains("future", error.Message);
        Assert.Contains("2.0.0", error.Message);
        Assert.Contains(ExtensionApi.Current, error.Message);
    }

    [Fact]
    public void Discover_replaces_the_previous_result()
    {
        using var temp = new TempDirectory();
        TestExtensions.WriteManifest(
            TestExtensions.GlobalExtensionDirectory(temp, "first"),
            "first"
        );
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        string second = temp.Subdirectory("second-project");
        TestExtensions.WriteManifest(
            TestExtensions.ProjectExtensionDirectory(second, "second"),
            "second"
        );
        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(second, "repo");

        Assert.Equal(["first", "second"], descriptors.Select(descriptor => descriptor.Id));
    }
}
