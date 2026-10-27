using System.Text;
using System.Text.Json.Nodes;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// What a trust-store update may and may not do (spec: "The trust store"). The file is the only
/// copy of every decision the user has made, so an update that cannot read it refuses instead of
/// replacing it, and updates that run at once — another session, another process — merge instead of
/// overwriting each other.
/// </summary>
public sealed class TrustStoreDurabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private const string Damaged =
        "{\n  \"repositories\": {\n    \"repo-1\": {\n      \"worktrees\": {\n";

    [Fact]
    public void A_damaged_store_is_preserved_byte_for_byte_when_an_approval_fails()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string trustPath = Path.Combine(store, "trust.json");
        byte[] original = Encoding.UTF8.GetBytes(Damaged);
        File.WriteAllBytes(trustPath, original);
        var trust = new ExtensionTrustStore(TestExtensions.Options(store));

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            trust.RecordTrusted("repo-2", "/worktree", "hello", "hash", Now)
        );

        Assert.Contains(trustPath, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(trustPath));
        // Nothing else appeared either: no half-written temporary, only the claim the next writer
        // waits for.
        Assert.Equal(
            ["trust.json", "trust.lock"],
            Directory.GetFiles(store).Select(Path.GetFileName).Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void A_damaged_store_refuses_a_read_instead_of_reading_as_empty()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string trustPath = Path.Combine(store, "trust.json");
        File.WriteAllText(trustPath, Damaged);
        var trust = new ExtensionTrustStore(TestExtensions.Options(store));

        // Fail closed: the question is refused with the reason, never answered "untrusted" — an
        // empty store would re-prompt for everything and then write the empty decision back.
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            trust.Evaluate("repo-1", "/worktree", "hello", "hash")
        );
        Assert.Contains(trustPath, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("not valid JSON", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_store_that_is_not_a_json_object_is_refused()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string trustPath = Path.Combine(store, "trust.json");
        File.WriteAllText(trustPath, "[]");
        var trust = new ExtensionTrustStore(TestExtensions.Options(store));

        Assert.Contains(
            "does not hold a JSON object",
            Assert
                .Throws<InvalidOperationException>(() =>
                    trust.RecordSingleFileTrust("/extensions/a.cs", "digest")
                )
                .Message,
            StringComparison.Ordinal
        );
        Assert.Equal("[]", File.ReadAllText(trustPath));
    }

    [Fact]
    public async Task Two_concurrent_approvals_both_survive()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var temp = new TempDirectory();
            string store = temp.Subdirectory("store");
            var trust = new ExtensionTrustStore(TestExtensions.Options(store));
            using var start = new Barrier(2);

            void Approve(string repository, string worktree, string id)
            {
                start.SignalAndWait();
                trust.RecordTrusted(repository, worktree, id, $"hash-{id}", Now);
            }

            CancellationToken cancellation = TestContext.Current.CancellationToken;
            Task first = Task.Run(() => Approve("repo-a", "/worktree-a", "ext-a"), cancellation);
            Task second = Task.Run(() => Approve("repo-b", "/worktree-b", "ext-b"), cancellation);
            await Task.WhenAll(first, second);

            // Both decisions are in the file: neither read-modify-write wrote a document that had
            // not seen the other's.
            JsonNode document = JsonNode.Parse(
                File.ReadAllText(Path.Combine(store, "trust.json"))
            )!;
            JsonNode repositories = document["repositories"]!;
            Assert.Equal(
                "hash-ext-a",
                repositories["repo-a"]!["worktrees"]!["/worktree-a"]!["ext-a"]!.GetValue<string>()
            );
            Assert.Equal(
                "hash-ext-b",
                repositories["repo-b"]!["worktrees"]!["/worktree-b"]!["ext-b"]!.GetValue<string>()
            );
        }
    }

    [Fact]
    public async Task An_approval_waits_for_a_claim_another_writer_holds()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string trustPath = Path.Combine(store, "trust.json");
        var trust = new ExtensionTrustStore(TestExtensions.Options(store));
        trust.RecordTrusted("repo-1", "/worktree-1", "ext-1", "hash-1", Now);
        byte[] settled = File.ReadAllBytes(trustPath);

        // Another writer's claim — the same exclusive handle on the same lock file a second process
        // takes — is held while the approval runs, so the approval may not read, change or write.
        string lockPath = Path.ChangeExtension(trustPath, ".lock");
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        Task approvalHolder;
        using (
            new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
        )
        {
            Task approval = Task.Run(
                () => trust.RecordTrusted("repo-2", "/worktree-2", "ext-2", "hash-2", Now),
                cancellation
            );

            await Task.Delay(300, cancellation);

            Assert.False(approval.IsCompleted, "the approval must wait for the claim");
            Assert.Equal(settled, File.ReadAllBytes(trustPath));
            approvalHolder = approval;
        }

        await approvalHolder.WaitAsync(TestContext.Current.CancellationToken);

        // The claim was released, the approval read the document the other writer left, and both
        // decisions are in the file.
        JsonNode repositories = JsonNode.Parse(File.ReadAllText(trustPath))!["repositories"]!;
        Assert.Equal(
            "hash-1",
            repositories["repo-1"]!["worktrees"]!["/worktree-1"]!["ext-1"]!.GetValue<string>()
        );
        Assert.Equal(
            "hash-2",
            repositories["repo-2"]!["worktrees"]!["/worktree-2"]!["ext-2"]!.GetValue<string>()
        );
    }
}
