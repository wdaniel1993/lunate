using System.Text;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The committed extension lock (repo-quality spec: "committed fixtures keep their bytes" and "a
/// lock's bytes carry no carriage return"): written once on POSIX, read on every CI leg, and the
/// same hash on all three — which is what makes the lock's trust digest a contract rather than a
/// local accident.
/// </summary>
public sealed class CommittedDigestFixtureTests
{
    /// <summary>SHA-256, hex lower, of <c>tests/fixtures/digests/extension.lock.json</c>.</summary>
    private const string LockHash =
        "06758acd7462d4a03719e43de0b55acab5e44c114ae062b9887b990c07929a4d";

    [Fact]
    public void The_committed_lock_carries_LF_and_hashes_to_the_pinned_value()
    {
        string path = Fixture("extension.lock.json");

        byte[] bytes = File.ReadAllBytes(path);

        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.EndsWith("\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal(LockHash, ExtensionLockJson.Hash(bytes));
    }

    [Fact]
    public void The_committed_lock_parses_and_re_writes_to_the_same_bytes()
    {
        string path = Fixture("extension.lock.json");
        byte[] bytes = File.ReadAllBytes(path);

        ExtensionLock parsed = ExtensionLockJson.TryParse(
            Encoding.UTF8.GetString(bytes),
            out string? error
        )!;

        Assert.Null(error);
        Assert.Equal("acme-tools", parsed.ExtensionId);
        Assert.Equal(bytes, ExtensionLockJson.ToBytes(parsed));
        Assert.DoesNotContain((byte)'\r', ExtensionLockJson.GraphBytes(parsed));
    }

    private static string Fixture(string name)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "lunate.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return Path.Combine(root, "tests", "fixtures", "digests", name);
    }
}
