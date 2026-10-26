namespace Lunate.Extensibility.Tests;

/// <summary>
/// Fail closed on a damaged install record (design D7/D8): a record that cannot be read or whose
/// integrity hash no longer answers for its bytes must never make a user-scope extension look
/// hand-placed, because placement is what trusts it.
/// </summary>
public sealed class BrokenInstallRecordTests
{
    [Fact]
    public async Task Invalid_json_in_the_store_leaves_the_extension_unloaded_and_reports_it_broken()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        TestExtensions.InstallHelloExtension(temp, "hello");
        string record = BrokenRecord(store, "acme.toolkit", "{ this is not json");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(store, log));
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

        Assert.Contains(record, refused.Message, StringComparison.Ordinal);
        Assert.Contains("broken", refused.Message, StringComparison.Ordinal);
        Assert.Contains("lunate packages --verify", refused.Message, StringComparison.Ordinal);
        Assert.Contains("reinstall", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("created hello instance", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task An_integrity_mismatch_leaves_the_extension_unloaded_and_reports_it_broken()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string extension = TestExtensions.InstallHelloExtension(temp, "hello");
        string record = HealthyRecord(store, extension, "acme.toolkit", "hello");
        File.WriteAllText(
            record,
            File.ReadAllText(record).Replace("\"0.1.0\"", "\"0.1.1\"", StringComparison.Ordinal)
        );
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(store, log));
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

        Assert.Contains(record, refused.Message, StringComparison.Ordinal);
        Assert.Contains("broken", refused.Message, StringComparison.Ordinal);
        Assert.Contains("integrity", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("created hello instance", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_healthy_record_still_loads_with_the_normal_trust_question()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string extension = TestExtensions.InstallHelloExtension(temp, "hello");
        _ = HealthyRecord(store, extension, "acme.toolkit", "hello");
        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(temp.Root, "repo-1");
        var prompt = new FakeExtensionTrustPrompt(approve: true);

        LoadedExtension loaded = await loader.Load(
            "hello",
            temp.Root,
            "repo-1",
            prompt,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("hello", loaded.Id);
        Assert.Equal(1, prompt.Calls);
        Assert.Contains("from package acme.toolkit@1.4.2", prompt.LastFromPackage);
    }

    /// <summary>
    /// A canonical record for the extension named by <paramref name="extension"/>, written through
    /// the one writer so its integrity hash answers for its bytes. Its paths are the extension's own
    /// files, recorded the way an install records them.
    /// </summary>
    private static string HealthyRecord(
        string store,
        string extension,
        string packageId,
        string extensionId
    )
    {
        List<InstalledFile> files = [];
        foreach (
            string path in Directory
                .GetFiles(extension, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
        )
        {
            string relative = Path.GetRelativePath(store, path)
                .Replace(Path.DirectorySeparatorChar, '/');
            byte[] bytes = File.ReadAllBytes(path);
            files.Add(new InstalledFile(relative, bytes.Length, Convert.ToHexStringLower(bytes)));
        }

        string record = Path.Combine(
            store,
            "packages",
            packageId.ToLowerInvariant(),
            PackageInstallRecordJson.FileName
        );
        PackageInstallRecordJson.WriteTo(
            record,
            new PackageInstallRecord
            {
                Package = new InstalledPackage
                {
                    Id = packageId,
                    Version = "1.4.2",
                    Sha512 = "c2hhNTEy",
                    Sources = ["local"],
                    Files = [.. files.Select(file => file.Path)],
                    ExtensionId = extensionId,
                },
                Content = new InstalledContent
                {
                    ExtensionId = extensionId,
                    ExtensionVersion = "0.1.0",
                    Skills = [],
                    Paths = files,
                },
                Scope = PackageScope.User,
                InstalledAt = "2026-01-02T03:04:05.0000000+00:00",
            }
        );
        return record;
    }

    private static string BrokenRecord(string store, string packageId, string content)
    {
        string record = Path.Combine(
            store,
            "packages",
            packageId.ToLowerInvariant(),
            PackageInstallRecordJson.FileName
        );
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        File.WriteAllText(record, content);
        return record;
    }
}
