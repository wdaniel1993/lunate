using System.Text;
using System.Text.Json;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// Task 1 and 2 of the branching-sessions change at the format seam: the two tree kinds round-trip,
/// a dangling leaf is refused, and a session that never moved its leaf digests exactly as the
/// single-path rule always did — the reference implementation below is the pre-change algorithm,
/// written from the raw lines, so the comparison cannot drift with the production code.
/// </summary>
public sealed class SessionTreeFormatTests
{
    private static string SessionsDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "sessions");

    private static string[] SinglePathFixtures =>
        [
            "golden-text.jsonl",
            "golden-tool-call.jsonl",
            "golden-mixed.jsonl",
            "golden-reasoning.jsonl",
            "golden-v2-header.jsonl",
            "golden-v2-entries.jsonl",
            "golden-v2-extension.jsonl",
            "golden-v3-usage.jsonl",
            "golden-v3-image.jsonl",
        ];

    [Fact]
    public void The_branch_golden_round_trips_its_tree_kinds_byte_for_byte()
    {
        string path = Path.Combine(SessionsDirectory, "golden-branch-tree.jsonl");
        string[] lines = File.ReadAllLines(path);

        string roundTripped =
            string.Join(
                '\n',
                lines.Select(line => SessionFormat.Serialize(SessionFormat.Parse(line)))
            ) + "\n";

        Assert.Equal(File.ReadAllText(path), roundTripped);

        // The two new kinds are the point of this golden: both parse into their own records.
        SessionActiveLeafEntry move = Assert.Single(
            lines.Select(SessionFormat.Parse).OfType<SessionActiveLeafEntry>()
        );
        Assert.Equal("e_02", move.LeafId);
        Assert.Equal("e_03", move.ParentId);
        SessionBranchSummaryEntry summary = Assert.Single(
            lines.Select(SessionFormat.Parse).OfType<SessionBranchSummaryEntry>()
        );
        Assert.Equal("e_02", summary.ParentId);
        Assert.Equal("e_03", summary.FromLeafId);
        Assert.Equal("Approach B was started and abandoned.", summary.Summary);
    }

    [Fact]
    public void The_branch_golden_records_its_origin_and_branched_header()
    {
        string path = Path.Combine(SessionsDirectory, "golden-branch-tree.jsonl");

        SessionHeaderEntry header = Assert.IsType<SessionHeaderEntry>(
            SessionFormat.Parse(File.ReadAllLines(path)[0])
        );

        Assert.Equal(3, header.Schema);
        Assert.Equal(new SessionOrigin("s_20261020-083000-a1b2", "e_02"), header.Origin);
        Assert.True(header.Branched);
    }

    [Theory]
    [InlineData("golden-v2-header.jsonl")]
    [InlineData("golden-v3-usage.jsonl")]
    public void A_session_without_an_active_leaf_loads_and_digests_exactly_as_before(string name)
    {
        string path = Path.Combine(SessionsDirectory, name);

        Session loaded = Session.Load(path);

        Assert.Equal(Described(OldRuleHistory(path)), Described(loaded.ToHistory()));
        Assert.Equal(
            FixtureFormat.ComputeRequestDigest(OldRuleHistory(path), null),
            FixtureFormat.ComputeRequestDigest(loaded.ToHistory(), null)
        );
    }

    [Fact]
    public void A_single_path_histories_digest_is_pinned()
    {
        Session loaded = Session.Load(Path.Combine(SessionsDirectory, "golden-v3-usage.jsonl"));

        // Pinned: the request the pre-change rule produces for this file's two messages. The oracle
        // below (OldRuleHistory) proves the equality structurally; this constant catches a change in
        // the digest's input that the oracle and the production path could only make together.
        Assert.Equal(
            "51d879ce49c95e89f52ec288b47ad889b1a8f705a20bd3cbaec59d9e7d840090",
            FixtureFormat.ComputeRequestDigest(loaded.ToHistory(), null)
        );
    }

    [Theory]
    [InlineData("golden-text.jsonl")]
    [InlineData("golden-tool-call.jsonl")]
    [InlineData("golden-mixed.jsonl")]
    [InlineData("golden-reasoning.jsonl")]
    [InlineData("golden-v2-entries.jsonl")]
    [InlineData("golden-v2-extension.jsonl")]
    public void Every_single_path_fixture_keeps_the_pre_change_request(string name)
    {
        string path = Path.Combine(SessionsDirectory, name);

        List<ChatMessage> expected = OldRuleHistory(path);
        List<ChatMessage> actual = Session.Load(path).ToHistory();

        Assert.Equal(Described(expected), Described(actual));
        Assert.Equal(
            FixtureFormat.ComputeRequestDigest(expected, null),
            FixtureFormat.ComputeRequestDigest(actual, null)
        );
    }

    /// <summary>
    /// The image golden keeps its messages but not a replay digest: the fixture format hashes image
    /// bytes as <c>DataContent</c> and cannot serialize a session's <see cref="ImageContentReference"/>,
    /// so the image fixture never took part in request-digest replay (ADR-0018 addendum).
    /// </summary>
    [Fact]
    public void The_image_fixture_keeps_every_message_on_its_single_path()
    {
        string path = Path.Combine(SessionsDirectory, "golden-v3-image.jsonl");

        Assert.Equal(Described(OldRuleHistory(path)), Described(Session.Load(path).ToHistory()));
    }

    [Fact]
    public void A_session_whose_leaf_was_moved_sends_only_that_path()
    {
        Session loaded = Session.Load(Path.Combine(SessionsDirectory, "golden-branch-tree.jsonl"));

        List<ChatMessage> history = loaded.ToHistory();

        Assert.Equal(
            [
                "Compare two approaches.",
                "Approach A first.",
                "Approach B was started and abandoned.",
                "Continue with approach A.",
            ],
            history.Select(message => message.Text)
        );
        Assert.Equal("e_06", loaded.ActiveLeafId);
        Assert.Equal(["e_01", "e_02", "e_05", "e_06"], loaded.ActivePath.Select(entry => entry.Id));
    }

    [Fact]
    public void An_active_leaf_naming_an_unknown_entry_is_refused_naming_the_file_and_the_id()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(
            path,
            """
            {"type":"header","schema":3,"id":"s_20261020-090000-b7c8","cwd":"/repo","created":"2026-10-20T09:00:00.0000000+00:00","meai":"10.10.1"}
            {"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-20T09:00:01.0000000+00:00","message":{"role":"user","contents":[{"$type":"text","text":"one"}]}}
            {"type":"activeLeaf","id":"e_02","parentId":"e_01","timestamp":"2026-10-20T09:00:02.0000000+00:00","leafId":"e_99"}

            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Session.Load(path)
        );

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("e_99", exception.Message, StringComparison.Ordinal);
        Assert.Contains("active leaf", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_session_writes_schema_4_and_no_provenance()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(SessionImageTestData.Start)
        );

        SessionHeaderEntry header = session.Header;

        Assert.Equal(4, header.Schema);
        Assert.Null(header.Origin);
        Assert.False(header.Branched);
    }

    /// <summary>
    /// The pre-change history rule, written from the raw lines: the last compaction's summary first,
    /// then every message entry in file order that the summary does not replace. Header-only
    /// sessions and anything the rule never sent stay out.
    /// </summary>
    private static List<ChatMessage> OldRuleHistory(string path)
    {
        string[] lines = File.ReadAllLines(path).Where(line => line.Length > 0).ToArray();
        List<(string Id, ChatMessage Message)> messages = [];
        string? summary = null;
        HashSet<string>? replaced = null;
        foreach (string line in lines.Skip(1))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            string type = root.GetProperty("type").GetString()!;
            if (type == "message")
            {
                ChatMessage message = JsonSerializer.Deserialize<ChatMessage>(
                    root.GetProperty("message").GetRawText(),
                    SessionJson.Options
                )!;
                messages.Add((root.GetProperty("id").GetString()!, message));
            }
            else if (type == "compaction")
            {
                summary = root.GetProperty("summary").GetString();
                replaced =
                [
                    .. root.GetProperty("replaces").EnumerateArray().Select(id => id.GetString()!),
                ];
            }
        }

        List<ChatMessage> history = [];
        if (summary is not null)
        {
            history.Add(new ChatMessage(ChatRole.Assistant, summary));
        }

        history.AddRange(
            messages
                .Where(entry => replaced is null || !replaced.Contains(entry.Id))
                .Select(entry => entry.Message)
        );
        return history;
    }

    private static List<string> Described(IEnumerable<ChatMessage> messages) =>
        [
            .. messages.Select(message =>
                message.Role.Value + ":" + message.Text + ":" + message.Contents.Count
            ),
        ];

    private const string PinnedDigest = "__PINNED__";
}
