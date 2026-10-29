using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The discovery side of the built-in provenance (spec: Built-in extension provenance): the host
/// composition folds its built-in list in before the store scans, a directory manifest cannot claim
/// built-in provenance, and a discovered id cannot shadow a built-in.
/// </summary>
public sealed class ExtensionBuiltInDiscoveryTests
{
    private const string First =
        """{"id":"alpha","version":"0.1.0","apiVersion":"^1.0.0","builtin":true}""";
    private const string Second =
        """{"id":"beta","version":"0.2.0","apiVersion":"^1.0.0","builtin":true}""";

    [Fact]
    public void Builtins_fold_in_before_store_scans_in_declared_order_even_without_a_store()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string alpha = $"{store}/host/alpha/extension.json";
        string beta = $"{store}/host/beta/extension.json";
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = store,
                BuiltIns =
                [
                    new BuiltInExtension(
                        alpha,
                        ExtensionManifest.Parse(First, alpha),
                        () => SeedFactory
                    ),
                    new BuiltInExtension(
                        beta,
                        ExtensionManifest.Parse(Second, beta),
                        () => SeedFactory
                    ),
                ],
            }
        );

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        Assert.Empty(loader.DiscoveryErrors);
        Assert.Equal(["alpha", "beta"], descriptors.Select(descriptor => descriptor.Id));
        Assert.All(
            descriptors,
            descriptor => Assert.Equal(ExtensionScope.Builtin, descriptor.Scope)
        );
        Assert.All(descriptors, descriptor => Assert.Null(descriptor.Directory));
        Assert.Equal("alpha", descriptors[0].Manifest.Id);
        Assert.Equal("0.2.0", descriptors[1].Manifest.Version);
    }

    [Fact]
    public void A_discovered_manifest_still_follows_the_builtins()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        TestExtensions.WriteManifest(
            TestExtensions.GlobalExtensionDirectory(temp, "gamma"),
            "gamma"
        );
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = store,
                BuiltIns = [BuiltIn("alpha", $"{store}/host/alpha/extension.json")],
            }
        );

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        Assert.Equal(["alpha", "gamma"], descriptors.Select(descriptor => descriptor.Id));
        Assert.Equal(ExtensionScope.Builtin, descriptors[0].Scope);
    }

    [Fact]
    public void A_directory_manifest_declaring_builtin_cannot_enable_built_in_provenance()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string directory = TestExtensions.GlobalExtensionDirectory(temp, "sneaky");
        Directory.CreateDirectory(directory);
        string manifestPath = Path.Combine(directory, "extension.json");
        File.WriteAllText(
            manifestPath,
            """{"id":"sneaky","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll","builtin":true}"""
        );
        var loader = new ExtensionLoader(TestExtensions.Options(store));

        Assert.Empty(loader.Discover(temp.Root, "repo"));

        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Equal(manifestPath, error.Path);
        Assert.Equal(
            $"{manifestPath}: field 'builtin' of a discovered manifest cannot be enabled; built-ins are supplied by the host.",
            error.Message
        );
    }

    [Fact]
    public void A_host_list_entry_without_builtin_true_refuses_the_composition()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        const string path = "host/silent/extension.json";
        const string json =
            """{"id":"silent","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll"}""";
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = store,
                BuiltIns =
                [
                    new BuiltInExtension(
                        path,
                        ExtensionManifest.Parse(json, path),
                        () => SeedFactory
                    ),
                ],
            }
        );

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        Assert.Empty(descriptors);
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Equal(path, error.Path);
        Assert.Equal(
            $"the built-in manifest '{path}' does not declare \"builtin\": true; the host composition refused it.",
            error.Message
        );
    }

    [Fact]
    public void A_discovered_id_cannot_shadow_a_builtin_and_one_error_names_both_sources()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        TestExtensions.WriteManifest(
            TestExtensions.GlobalExtensionDirectory(temp, "hello"),
            "hello"
        );
        string manifestPath = Path.Combine(
            TestExtensions.GlobalExtensionDirectory(temp, "hello"),
            "extension.json"
        );
        const string builtInPath = "host/hello/extension.json";
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = store,
                BuiltIns =
                [
                    new BuiltInExtension(
                        builtInPath,
                        BuiltInManifest("hello", builtInPath),
                        () => SeedFactory
                    ),
                ],
            }
        );

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        ExtensionDescriptor descriptor = Assert.Single(descriptors);
        Assert.Equal(ExtensionScope.Builtin, descriptor.Scope);
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Equal(manifestPath, error.Path);
        Assert.Contains(builtInPath, error.Message, StringComparison.Ordinal);
        Assert.Contains(manifestPath, error.Message, StringComparison.Ordinal);
        Assert.Contains("a built-in is never shadowed", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("declared more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_discovered_conflict_between_two_store_directories_keeps_its_old_message()
    {
        using var temp = new TempDirectory();
        string global = TestExtensions.GlobalExtensionDirectory(temp, "dupe");
        string project = TestExtensions.ProjectExtensionDirectory(temp.Root, "dupe");
        TestExtensions.WriteManifest(global, "dupe");
        TestExtensions.WriteManifest(project, "dupe");
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));

        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(temp.Root, "repo");

        Assert.Single(descriptors);
        Assert.Equal("dupe", descriptors[0].Id);
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains("declared more than once", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "entryAssembly",
        """{"id":"supplied","version":"0.1.0","apiVersion":"^1.0.0","builtin":true,"entryAssembly":"e.dll"}"""
    )]
    [InlineData(
        "runtime",
        """{"id":"supplied","version":"0.1.0","apiVersion":"^1.0.0","builtin":true,"runtime":{"command":"node"}}"""
    )]
    public void A_supplied_builtin_claiming_out_of_process_provenance_is_refused(
        string field,
        string json
    )
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        const string path = "host/supplied/extension.json";
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = store,
                BuiltIns =
                [
                    new BuiltInExtension(
                        path,
                        ExtensionManifest.Parse(json, path),
                        () => SeedFactory
                    ),
                ],
            }
        );

        Assert.Empty(loader.Discover(temp.Root, "repo"));
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{field}'", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            "a built-in runs in the default load context",
            error.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void An_incompatible_builtin_apiVersion_is_refused_at_discovery()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = store,
                BuiltIns =
                [
                    new BuiltInExtension(
                        "host/future/extension.json",
                        BuiltInManifest("future", "host/future/extension.json", "2.0.0"),
                        () => SeedFactory
                    ),
                ],
            }
        );

        Assert.Empty(loader.Discover(temp.Root, "repo"));
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains("future", error.Message, StringComparison.Ordinal);
        Assert.Contains("2.0.0", error.Message, StringComparison.Ordinal);
        Assert.Contains(ExtensionApi.Current, error.Message, StringComparison.Ordinal);
    }

    internal static BuiltInExtension BuiltIn(
        string id,
        string path,
        string apiVersion = "^1.0.0"
    ) => new(path, BuiltInManifest(id, path, apiVersion), () => SeedFactory);

    internal static ExtensionManifest BuiltInManifest(
        string id,
        string path,
        string apiVersion = "^1.0.0"
    ) =>
        ExtensionManifest.Parse(
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"{{apiVersion}}","builtin":true}""",
            path
        );

    private static IExtensionFactory SeedFactory =>
        throw new InvalidOperationException("discovery must never run a built-in factory");
}
