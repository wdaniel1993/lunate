using System.Text.Json.Nodes;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The trust/restore ordering (spec: "Trust before restore"): the first trust restores, writes the
/// lock and binds its graph into the trust record without a second prompt; a source edit and a
/// hand-edited <b>graph</b> both make the file stale and stop before any feed request; a
/// <b>toolset</b> change rebuilds silently, because it is not part of what the user approved.
/// </summary>
public sealed class SingleFileExtensionRestoreTests
{
    private const string WithPackage = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue
        #:package Octokit@14.0.0

        public static class Acme
        {
            public static void Register() { }
        }

        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_trust_restores_binds_the_lock_and_needs_no_second_prompt()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", WithPackage);
        var engine = new CountingRestoreEngine(_ => Result("Octokit", "14.0.0"));
        var factory = new CountingEngineFactory(engine);
        var prompt = new RecordingSingleFilePrompt(true);
        SingleFileExtensionRestorer restorer = fixture.Restorer(factory, prompt);

        SingleFileRestoreOutcome outcome = await restorer.EnsureRestoredAsync(
            path,
            ExtensionScope.Global,
            prompt,
            Ct
        );

        Assert.Equal(SingleFileRestoreStatus.Restored, outcome.Status);
        Assert.Equal(1, prompt.Calls);
        Assert.Equal(1, factory.Creations);
        Assert.Equal(1, engine.Requests);
        Assert.Equal("acme-tools", Assert.Single(engine.Seen).ExtensionId);
        Assert.Contains("Octokit@14.0.0", prompt.Requests[0].Packages, StringComparer.Ordinal);

        // The lock is the cache entry's, never a sidecar beside the user's one file.
        string lockPath = Path.Combine(
            fixture.Store,
            "cache",
            "extensions",
            "acme-tools",
            "extension.lock.json"
        );
        Assert.Equal(lockPath, outcome.LockPath);
        Assert.True(File.Exists(lockPath));
        Assert.False(File.Exists(Path.ChangeExtension(path, ".lock.json")));
        Assert.Equal("14.0.0", Assert.Single(outcome.Lock!.Packages).Version);

        // The bind: the trust record now holds the digest that covers the lock's graph, and a fresh
        // discovery agrees it is trusted (it reads NotBuilt, not Stale).
        byte[] lockBytes = File.ReadAllBytes(lockPath);
        string bound = SingleFileExtensionDigest.Compute(
            "acme-tools",
            [new ExtensionPackageReference("Octokit", "14.0.0", "Octokit@14.0.0")],
            File.ReadAllBytes(path),
            ExtensionLockJson.GraphBytes(outcome.Lock)
        );
        Assert.Equal(bound, RecordedDigest(fixture, path));
        SingleFileExtension reread = Assert.Single(fixture.Discover());
        Assert.Equal(SingleFileExtensionState.NotBuilt, reread.State);
        Assert.Equal(bound, reread.Digest);
        Assert.Equal(ExtensionLockJson.Hash(lockBytes), reread.LockHash);

        // A later restore is a lock read: no engine, no prompt, no feed.
        var never = new CountingEngineFactory(engine: null);
        SingleFileRestoreOutcome again = await fixture
            .Restorer(never, prompt: null)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt: null, Ct);
        Assert.Equal(SingleFileRestoreStatus.Restored, again.Status);
        Assert.Equal(0, never.Creations);
        Assert.Equal(1, engine.Requests);
        Assert.Equal(1, prompt.Calls);
        Assert.Equal(bound, again.Candidate.Digest);
    }

    [Fact]
    public async Task A_source_edit_stops_before_any_feed_request()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", WithPackage);
        await RestoreOnce(fixture, path);

        File.AppendAllText(path, "// one byte more\n");
        CountingEngineFactory factory = new(
            new CountingRestoreEngine(_ => Result("Octokit", "14.0.0"))
        );

        SingleFileExtension stale = Assert.Single(fixture.Discover());
        Assert.Equal(SingleFileExtensionState.Stale, stale.State);

        SingleFileRestoreOutcome outcome = await fixture
            .Restorer(factory, prompt: null)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt: null, Ct);

        Assert.Equal(SingleFileRestoreStatus.TrustRequired, outcome.Status);
        Assert.Equal(0, factory.Creations);
        Assert.Equal(SingleFileExtensionState.Stale, outcome.Candidate.State);
        Assert.Contains("not trusted", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hand_edited_lock_is_stale_and_the_next_restore_asks_once_without_a_feed()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", WithPackage);
        SingleFileRestoreOutcome first = await RestoreOnce(fixture, path);
        string lockPath = first.LockPath!;

        string text = File.ReadAllText(lockPath);
        File.WriteAllText(
            lockPath,
            text.Replace("\"14.0.0\"", "\"14.1.0\"", StringComparison.Ordinal)
        );

        SingleFileExtension stale = Assert.Single(fixture.Discover());
        Assert.Equal(SingleFileExtensionState.Stale, stale.State);
        Assert.Contains("lock", stale.Reason!, StringComparison.OrdinalIgnoreCase);

        CountingEngineFactory factory = new(
            new CountingRestoreEngine(_ => Result("Octokit", "14.0.0"))
        );
        SingleFileRestoreOutcome outcome = await fixture
            .Restorer(factory, prompt: null)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt: null, Ct);

        Assert.Equal(SingleFileRestoreStatus.TrustRequired, outcome.Status);
        Assert.Equal(0, factory.Creations);
        Assert.Contains("\"14.1.0\"", File.ReadAllText(lockPath), StringComparison.Ordinal);

        // Approved, the edited lock is the decision: it is adopted without a second question and
        // without a feed, because the user approved exactly this resolution.
        var prompt = new RecordingSingleFilePrompt(true);
        SingleFileRestoreOutcome accepted = await fixture
            .Restorer(factory, prompt)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt, Ct);

        Assert.Equal(SingleFileRestoreStatus.Restored, accepted.Status);
        Assert.Equal(1, prompt.Calls);
        Assert.Equal(0, factory.Creations);
        ExtensionLock adopted = ExtensionLockJson.TryParse(File.ReadAllText(lockPath), out _)!;
        Assert.Equal("14.1.0", Assert.Single(adopted.Packages).Version);
    }

    [Fact]
    public async Task A_graph_change_changes_the_digest_and_still_asks()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", WithPackage);
        SingleFileRestoreOutcome first = await RestoreOnce(fixture, path);
        string lockPath = first.LockPath!;
        string bound = RecordedDigest(fixture, path);

        // A hand edit to the resolution — the packages the user approved — with the toolset left
        // exactly as it was.
        string text = File.ReadAllText(lockPath);
        File.WriteAllText(
            lockPath,
            text.Replace(
                "\"version\": \"14.0.0\"",
                "\"version\": \"14.1.0\"",
                StringComparison.Ordinal
            )
        );

        SingleFileExtension edited = Assert.Single(fixture.Discover());
        Assert.NotEqual(bound, edited.Digest);
        Assert.Equal(SingleFileExtensionState.Stale, edited.State);
        Assert.NotNull(edited.Lock);

        CountingEngineFactory factory = new(
            new CountingRestoreEngine(_ => Result("Octokit", "14.0.0"))
        );
        SingleFileRestoreOutcome outcome = await fixture
            .Restorer(factory, prompt: null)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt: null, Ct);

        Assert.Equal(SingleFileRestoreStatus.TrustRequired, outcome.Status);
        Assert.Equal(0, factory.Creations);
        Assert.Contains("\"14.1.0\"", File.ReadAllText(lockPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_toolset_change_rebuilds_the_lock_without_a_question()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", WithPackage);
        SingleFileTarget original = SingleFileTarget.Of(
            new Version(10, 0, 13),
            SingleFileTarget.Current().Rid
        );
        SingleFileRestoreOutcome first = await RestoreOnce(fixture, path, original);
        string lockPath = first.LockPath!;
        byte[] lockedBytes = File.ReadAllBytes(lockPath);
        string bound = RecordedDigest(fixture, path);
        DateTime trustedAt = File.GetLastWriteTimeUtc(fixture.TrustFile);

        // The same file, the same resolution, on a host whose .NET patch moved the runtime.
        var engine = new CountingRestoreEngine(_ => Result("Octokit", "14.0.0"));
        var factory = new CountingEngineFactory(engine);
        var prompt = new RecordingSingleFilePrompt();
        SingleFileTarget patched = SingleFileTarget.Of(new Version(10, 0, 14), original.Rid);

        SingleFileRestoreOutcome outcome = await fixture
            .Restorer(factory, prompt, patched)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt, Ct);

        // The assertion that matters is the prompt count: this prompt has no scripted answer, so a
        // call is an exception as well as a count. A toolset change is not a trust question.
        Assert.Equal(SingleFileRestoreStatus.Restored, outcome.Status);
        Assert.Equal(0, prompt.Calls);

        // It did rebuild, and the reason is the toolset — reported, never asked.
        Assert.Equal(1, factory.Creations);
        Assert.Equal(1, engine.Requests);
        Assert.Empty(outcome.Changes);
        Assert.Contains(
            outcome.RebuildReasons,
            reason => reason.Contains("10.0.14", StringComparison.Ordinal)
        );

        ExtensionLock rebuilt = ExtensionLockJson.TryParse(File.ReadAllText(lockPath), out _)!;
        Assert.Equal("10.0.14", rebuilt.Toolset.Runtime);
        Assert.NotEqual(lockedBytes, File.ReadAllBytes(lockPath));

        // The graph half — what the user approved — is byte-identical under both toolsets, and the
        // trust record was neither rewritten nor changed.
        Assert.Equal(
            ExtensionLockJson.GraphBytes(first.Lock!),
            ExtensionLockJson.GraphBytes(rebuilt)
        );
        Assert.Equal(bound, RecordedDigest(fixture, path));
        Assert.Equal(bound, outcome.Candidate.Digest);
        Assert.Equal(trustedAt, File.GetLastWriteTimeUtc(fixture.TrustFile));
    }

    [Fact]
    public async Task A_tampered_toolset_block_is_refused_and_rebuilt_without_a_question()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", WithPackage);
        SingleFileRestoreOutcome first = await RestoreOnce(fixture, path);
        string lockPath = first.LockPath!;
        string bound = RecordedDigest(fixture, path);

        // Edit the toolset half by hand and leave its integrity hash alone: the block no longer
        // answers for itself, so it is refused — the graph is untouched, so nothing is re-asked.
        string rid = first.Lock!.Toolset.Rid;
        string text = File.ReadAllText(lockPath);
        Assert.Contains($"\"rid\": \"{rid}\"", text, StringComparison.Ordinal);
        File.WriteAllText(
            lockPath,
            text.Replace(
                $"\"rid\": \"{rid}\"",
                "\"rid\": \"tampered-rid\"",
                StringComparison.Ordinal
            )
        );

        SingleFileExtension refused = Assert.Single(fixture.Discover());
        Assert.Null(refused.Lock);
        Assert.Null(refused.LockHash);
        Assert.Equal(SingleFileExtensionState.NotBuilt, refused.State);
        Assert.Contains("toolset", refused.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bound, refused.Digest);

        DateTime trustedAt = File.GetLastWriteTimeUtc(fixture.TrustFile);
        var engine = new CountingRestoreEngine(_ => Result("Octokit", "14.0.0"));
        var factory = new CountingEngineFactory(engine);
        var prompt = new RecordingSingleFilePrompt();

        SingleFileRestoreOutcome outcome = await fixture
            .Restorer(factory, prompt)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt, Ct);

        Assert.Equal(SingleFileRestoreStatus.Restored, outcome.Status);
        Assert.Equal(0, prompt.Calls);
        Assert.Equal(1, factory.Creations);
        Assert.NotEmpty(outcome.RebuildReasons);

        // The rewrite put a block back that answers for itself, with the same graph the user
        // approved.
        ExtensionLock rebuilt = ExtensionLockJson.TryParse(
            File.ReadAllText(lockPath),
            out string? problem
        )!;
        Assert.Null(problem);
        Assert.Equal(rid, rebuilt.Toolset.Rid);
        Assert.Equal(
            ExtensionLockJson.GraphBytes(first.Lock),
            ExtensionLockJson.GraphBytes(rebuilt)
        );
        Assert.Equal(bound, RecordedDigest(fixture, path));
        Assert.Equal(trustedAt, File.GetLastWriteTimeUtc(fixture.TrustFile));
    }

    [Fact]
    public async Task A_refused_resolution_change_leaves_the_lock_exactly_as_it_was()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", WithPackage);
        SingleFileTarget original = SingleFileTarget.Of(
            new Version(10, 0, 13),
            SingleFileTarget.Current().Rid
        );
        SingleFileRestoreOutcome first = await RestoreOnce(fixture, path, original);
        string lockPath = first.LockPath!;
        string locked = File.ReadAllText(lockPath);

        // A restore that would resolve another package version: a graph change, which is a trust
        // question even when the toolset moved as well.
        var engine = new CountingRestoreEngine(_ => Result("Octokit", "14.1.0"));
        var prompt = new RecordingSingleFilePrompt(false);
        SingleFileTarget patched = SingleFileTarget.Of(new Version(10, 0, 14), original.Rid);

        SingleFileRestoreOutcome outcome = await fixture
            .Restorer(new CountingEngineFactory(engine), prompt, patched)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt, Ct);

        Assert.Equal(SingleFileRestoreStatus.Denied, outcome.Status);
        Assert.Equal(1, prompt.Calls);
        Assert.Contains(
            prompt.Requests[0].LockChanges,
            change => change.Contains("14.1.0", StringComparison.Ordinal)
        );
        Assert.Equal(locked, File.ReadAllText(lockPath));
    }

    [Fact]
    public async Task A_refused_restore_writes_no_lock_and_leaves_no_binding()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", WithPackage);
        var factory = new CountingEngineFactory(
            new CountingRestoreEngine(_ => Result("Octokit", "14.0.0"))
        );
        var prompt = new RecordingSingleFilePrompt(false);

        SingleFileRestoreOutcome outcome = await fixture
            .Restorer(factory, prompt)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt, Ct);

        Assert.Equal(SingleFileRestoreStatus.Denied, outcome.Status);
        Assert.Equal(0, factory.Creations);
        Assert.False(
            File.Exists(
                Path.Combine(
                    fixture.Store,
                    "cache",
                    "extensions",
                    "acme-tools",
                    "extension.lock.json"
                )
            )
        );
        Assert.False(File.Exists(fixture.TrustFile));
    }

    private static async Task<SingleFileRestoreOutcome> RestoreOnce(
        SingleFileFixture fixture,
        string path,
        SingleFileTarget? target = null
    )
    {
        var engine = new CountingRestoreEngine(_ => Result("Octokit", "14.0.0"));
        var prompt = new RecordingSingleFilePrompt(true);
        SingleFileRestoreOutcome outcome = await fixture
            .Restorer(new CountingEngineFactory(engine), prompt, target)
            .EnsureRestoredAsync(path, ExtensionScope.Global, prompt, Ct);
        Assert.Equal(SingleFileRestoreStatus.Restored, outcome.Status);
        return outcome;
    }

    private static SingleFileRestoreResult Result(string id, string version) =>
        new()
        {
            Packages = [FakePackage.Create(id, version)],
            ReferencePack = FakePackage.ReferencePack(),
            Sources = ["local"],
            GlobalPackagesFolder = "/packages",
        };

    /// <summary>The digest the trust store recorded for a file, read straight from trust.json.</summary>
    private static string RecordedDigest(SingleFileFixture fixture, string path)
    {
        JsonNode document = JsonNode.Parse(File.ReadAllText(fixture.TrustFile))!;
        return document["singleFile"]![path]!.GetValue<string>();
    }
}
