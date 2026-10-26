using System.Security.Cryptography;
using System.Text;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The committed install record (repo-quality spec: "committed fixtures keep their bytes"): written
/// once on POSIX, read and re-written on every CI leg, and the same bytes and the same integrity
/// hash on all three. The record is the one document whose integrity digest was already pinned in
/// #169; this fixture is the proof that the pin holds over bytes committed to the repository, not
/// only over bytes a test just produced.
/// </summary>
public sealed class CommittedDigestFixtureTests
{
    /// <summary>SHA-256, hex lower, of <c>tests/fixtures/digests/install.json</c>.</summary>
    private const string RecordHash =
        "a9cdf0d801aafca99a12a50cdfe7afed7bc674be9aa6092ac074ff2ade52f6b1";

    [Fact]
    public void The_committed_record_carries_LF_and_hashes_to_the_pinned_value()
    {
        string path = Fixture("install.json");

        byte[] bytes = File.ReadAllBytes(path);

        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.EndsWith("\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal(RecordHash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public void The_committed_record_verifies_and_re_writes_to_the_same_bytes()
    {
        string path = Fixture("install.json");

        PackageInstallRecord? record = PackageInstallRecordJson.TryRead(path, out string? problem);

        Assert.Null(problem);
        Assert.NotNull(record);
        Assert.Equal("Acme.Toolkit", record.Package.Id);
        Assert.Equal(File.ReadAllBytes(path), PackageInstallRecordJson.ToBytes(record));
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
