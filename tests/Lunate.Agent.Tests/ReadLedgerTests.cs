namespace Lunate.Agent.Tests;

public sealed class ReadLedgerTests
{
    [Fact]
    public void A_later_read_supersedes_the_older_call()
    {
        ReadLedger ledger = new();
        string path = Path.GetFullPath("src/file.txt");
        ledger.Record(path, "first", "read-1", "src/file.txt");

        ledger.Record(path, "second", "read-2", "src/file.txt");

        Assert.Contains("superseded by a later read", ledger.StaleReadNotices["read-1"]);
        Assert.DoesNotContain("read-2", ledger.StaleReadNotices.Keys);
        Assert.Null(ledger.Check(path, "second", "src/file.txt"));
    }

    [Fact]
    public void A_published_change_invalidates_even_when_the_content_digest_is_unchanged()
    {
        ReadLedger ledger = new();
        string path = Path.GetFullPath("src/file.txt");
        ledger.Record(path, "line\r\n", "read-1", "src/file.txt");
        Assert.Null(ledger.Check(path, "line\n", "src/file.txt"));

        ledger.Invalidate(path);

        Assert.Contains("changed after this read", ledger.StaleReadNotices["read-1"]);
        Assert.Contains("changed after your last read", ledger.Check(path, "line\n", "src/file.txt"));
    }

    [Fact]
    public void An_out_of_band_change_invalidates_the_live_read()
    {
        ReadLedger ledger = new();
        string path = Path.GetFullPath("src/file.txt");
        ledger.Record(path, "before", "read-1", "src/file.txt");

        string? refusal = ledger.Check(path, "after", "src/file.txt");

        Assert.Contains("src/file.txt changed after your last read", refusal);
        Assert.Contains("re-read", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("src/file.txt changed after this read", ledger.StaleReadNotices["read-1"]);
    }

    [Fact]
    public void A_seeded_read_requires_a_fresh_read()
    {
        ReadLedger ledger = new();
        string path = Path.GetFullPath("src/file.txt");
        ledger.Seed(path, "read-1", "src/file.txt");

        Assert.Contains("changed after your last read", ledger.Check(path, "contents", "src/file.txt"));
        Assert.Contains("src/file.txt", ledger.StaleReadNotices["read-1"]);
    }

    [Fact]
    public void A_path_without_a_read_record_is_allowed()
    {
        ReadLedger ledger = new();

        Assert.Null(ledger.Check(Path.GetFullPath("src/file.txt"), "contents", "src/file.txt"));
    }
}
