namespace Lunate.Extensibility.Tests;

/// <summary>
/// A discovered extension directory is confined to its scope by its real path, not just by the
/// string the scan built: a symlink that leaves <c>extensions/</c> would load code from a place the
/// user never chose.
/// </summary>
public sealed class ExtensionScopeConfinementTests
{
    [Fact]
    public async Task A_symlinked_extension_directory_outside_the_scope_root_is_refused_by_name()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string outside = temp.Subdirectory("outside");
        TestExtensions.CopyFixture(outside, "HelloExtension.dll", "HelloExtension.Support.dll");
        TestExtensions.WriteManifest(outside, "hello");
        Directory.CreateDirectory(Path.Combine(store, "extensions"));
        string link = Path.Combine(store, "extensions", "hello");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }

        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(temp.Root, "repo-1");

        ExtensionLoadException refused = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load(
                "hello",
                temp.Root,
                "repo-1",
                new FakeExtensionTrustPrompt(approve: true),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(link, refused.Message, StringComparison.Ordinal);
        Assert.Contains("symbolic link", refused.Message, StringComparison.Ordinal);
    }
}
