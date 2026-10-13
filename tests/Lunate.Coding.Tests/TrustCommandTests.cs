using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The trust command in process: inspect prints and records nothing, --yes records exactly what it
/// printed, the digest gates recording, --json is the scripting contract, --clear prints what it
/// removed, and nothing ever loads extension code.
/// </summary>
public sealed class TrustCommandTests
{
    [Fact]
    public void Inspect_prints_the_pending_decisions_and_records_nothing()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp, withConfig: true);

        (int exitCode, string output, string errors) = fixture.Run();

        Assert.Equal(TrustCommand.ExitPending, exitCode);
        Assert.Empty(errors);
        Assert.Contains("trust: no records for this repository", output, StringComparison.Ordinal);
        Assert.Contains("pending:", output, StringComparison.Ordinal);
        Assert.Contains(
            "Load project extension 'hello' 0.1.0 from",
            output,
            StringComparison.Ordinal
        );
        Assert.Contains("Start 1 server from project mcp.json", output, StringComparison.Ordinal);
        Assert.Contains("  srv — x", output, StringComparison.Ordinal);
        Assert.Equal(2, fixture.PrintedHashes(output).Count);
        Assert.Contains("digest: ", output, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.TrustFile), "inspect must never write the trust file");
    }

    [Fact]
    public void Inspect_with_nothing_pending_exits_zero_and_prints_no_digest()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp);
        fixture.TrustEverything();

        (int exitCode, string output, _) = fixture.Run();

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.Contains("trust: nothing pending", output, StringComparison.Ordinal);
        Assert.DoesNotContain("digest:", output, StringComparison.Ordinal);
        Assert.Contains("trust: extensions — ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Yes_records_the_hashes_it_printed_in_both_sections()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp, withConfig: true);

        (int exitCode, string output, string errors) = fixture.Run("--yes");

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.Empty(errors);
        Assert.Contains("trust: recorded extensions — ", output, StringComparison.Ordinal);
        Assert.Contains("trust: recorded mcp.json — ", output, StringComparison.Ordinal);
        List<string> printed = fixture.PrintedHashes(output);
        Assert.Equal(2, printed.Count);
        Assert.All(
            fixture.Records(),
            record => Assert.Contains(record.ContentHash, printed, StringComparer.Ordinal)
        );
    }

    [Fact]
    public void A_second_yes_is_a_no_op_success()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp, withConfig: true);
        fixture.Run("--yes");

        (int exitCode, string output, _) = fixture.Run("--yes");

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.Contains("trust: nothing pending", output, StringComparison.Ordinal);
        Assert.Contains("trust: nothing to record", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_removes_the_records_and_prints_what_it_removed()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp, withConfig: true);
        fixture.Run("--yes");

        (int exitCode, string output, _) = fixture.Run("--clear");

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.Contains("trust: cleared extensions — ", output, StringComparison.Ordinal);
        Assert.Contains("trust: cleared mcp.json — ", output, StringComparison.Ordinal);
        Assert.Empty(fixture.Records());
        Assert.Equal(TrustCommand.ExitPending, fixture.Run().ExitCode);
    }

    [Fact]
    public void Clear_without_records_says_so()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp);

        (int exitCode, string output, _) = fixture.Run("--clear");

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.Contains("trust: no records to clear", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--yes", "--clear")]
    [InlineData("--json", "--yes")]
    [InlineData("--json", "--clear")]
    [InlineData("--digest", "abc")]
    [InlineData("--yes", "--digest")]
    [InlineData("--yes", "--yes")]
    [InlineData("stray")]
    public void Usage_errors_exit_two_with_nothing_on_stdout(params string[] args)
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp);

        (int exitCode, string output, string errors) = fixture.Run(args);

        Assert.Equal(TrustCommand.ExitUsage, exitCode);
        Assert.Empty(output);
        Assert.Contains("Usage: lunate trust", errors, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.TrustFile));
    }

    [Fact]
    public void A_changed_extension_re_prompts_with_the_changed_files_wording()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp);
        fixture.Run("--yes");
        File.WriteAllText(Path.Combine(fixture.ExtensionDirectory, "changed.txt"), "changed");

        (int exitCode, string output, _) = fixture.Run();

        Assert.Equal(TrustCommand.ExitPending, exitCode);
        Assert.Contains(
            "its files changed since this repository trusted it",
            output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Yes_records_a_new_worktree_entry_a_session_would_record_silently()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp);
        var store = new ExtensionTrustStore(new ExtensionHostOptions { StorePath = fixture.Store });
        store.RecordTrusted(
            fixture.Identity(),
            Path.Combine(fixture.Worktree, "another-worktree"),
            "a-previous-hash",
            DateTimeOffset.UtcNow
        );

        (int exitCode, string output, _) = fixture.Run("--yes");

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.Contains("trust: nothing pending", output, StringComparison.Ordinal);
        Assert.Contains("trust: recorded extensions — ", output, StringComparison.Ordinal);
        string hash = ExtensionTrustStore.ComputeContentHash(fixture.ExtensionDirectory);
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.Evaluate(fixture.Identity(), fixture.Worktree, hash)
        );
    }

    [Fact]
    public void A_matching_pinned_digest_records()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp, withConfig: true);
        string digest = fixture.PrintedDigest();

        (int exitCode, _, string errors) = fixture.Run("--yes", "--digest", digest);

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.Empty(errors);
        Assert.Equal(2, fixture.Records().Count);
    }

    [Fact]
    public void A_stale_pinned_digest_records_nothing_and_shows_the_difference()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp, withConfig: true);
        var stale = new string('f', 64);

        (int exitCode, string output, string errors) = fixture.Run("--yes", "--digest", stale);

        Assert.Equal(TrustCommand.ExitDigestMismatch, exitCode);
        Assert.Empty(errors);
        Assert.Contains($"pinned digest: {stale}", output, StringComparison.Ordinal);
        Assert.Contains("computed digest: ", output, StringComparison.Ordinal);
        Assert.Contains("pending:", output, StringComparison.Ordinal);
        Assert.Contains("trust: nothing was recorded", output, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.TrustFile));
    }

    [Fact]
    public void The_digest_is_stable_across_invocations_and_formatting_only_edits()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp, withConfig: true);
        string first = fixture.PrintedDigest();

        File.WriteAllText(
            fixture.ConfigPath,
            """
            { "mcpServers": { "srv": { "command": "x" } } }
            """
        );
        string second = fixture.PrintedDigest();

        File.WriteAllText(
            fixture.ConfigPath,
            """{ "mcpServers": { "srv": { "command": "y" } } }"""
        );
        string changed = fixture.PrintedDigest();

        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void Json_is_one_canonical_document_with_the_pending_set_and_digest()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp, withConfig: true);

        (int exitCode, string output, _) = fixture.Run("--json");

        Assert.Equal(TrustCommand.ExitPending, exitCode);
        Assert.DoesNotContain('\r', output);
        Assert.EndsWith("\n", output, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal(0, root.GetProperty("records").GetArrayLength());
        JsonElement pending = root.GetProperty("pending");
        Assert.Equal(2, pending.GetArrayLength());
        Assert.Equal("extension", pending[0].GetProperty("kind").GetString());
        Assert.Equal("hello", pending[0].GetProperty("id").GetString());
        Assert.False(pending[0].GetProperty("filesChanged").GetBoolean());
        Assert.Equal("mcp.json", pending[1].GetProperty("kind").GetString());
        Assert.Equal("srv", pending[1].GetProperty("servers")[0].GetProperty("name").GetString());
        Assert.Equal(fixture.PrintedDigest(), root.GetProperty("digest").GetString());
    }

    [Fact]
    public void Json_with_nothing_pending_carries_a_null_digest_and_exits_zero()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp);
        fixture.TrustEverything();

        (int exitCode, string output, _) = fixture.Run("--json");

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("digest").ValueKind);
        Assert.NotEqual(0, document.RootElement.GetProperty("records").GetArrayLength());
    }

    [Fact]
    public void Approval_never_loads_extension_code()
    {
        using var temp = new TempDirectory();
        Fixture fixture = Fixture.Create(temp);
        string marker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(fixture.Store, "hello", marker);

        (int exitCode, _, _) = fixture.Run("--yes");

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.False(File.Exists(marker), "the trust command must never load extension code");
        Assert.False(fixture.Log.Contains("created hello"));
        Assert.Single(fixture.Records());
    }

    private sealed class Fixture
    {
        private Fixture(string worktree, string store, bool withConfig)
        {
            Worktree = worktree;
            Store = store;
            if (withConfig)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(
                    ConfigPath,
                    """{ "mcpServers": { "srv": { "command": "x" } } }"""
                );
            }
        }

        public string Worktree { get; }

        public string Store { get; }

        public RecordingExtensionLog Log { get; } = new();

        public string TrustFile => Path.Combine(Store, "trust.json");

        public string ExtensionDirectory =>
            Path.Combine(Worktree, ".lunate", "extensions", "hello");

        public string ConfigPath => McpComposition.ProjectConfigPath(Worktree);

        public static Fixture Create(TempDirectory temp, bool withConfig = false)
        {
            string worktree = temp.File("worktree");
            Directory.CreateDirectory(Path.Combine(worktree, ".git"));
            HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
            return new Fixture(Workspace.Canonicalize(worktree), temp.File("store"), withConfig);
        }

        public (int ExitCode, string Output, string Errors) Run(params string[] args)
        {
            using var output = new StringWriter();
            using var errors = new StringWriter();
            int exitCode = TrustCommand.Run(
                args,
                Worktree,
                output,
                errors,
                new ExtensionSeams { StorePath = Store, Log = Log }
            );
            return (exitCode, Normalize(output.ToString()), Normalize(errors.ToString()));
        }

        public string Identity() => new Workspace(Worktree).GitCommonDir ?? Worktree;

        public IReadOnlyList<TrustRecord> Records() =>
            new ExtensionTrustStore(new ExtensionHostOptions { StorePath = Store }).Records(
                Identity()
            );

        public void TrustEverything()
        {
            var store = new ExtensionTrustStore(new ExtensionHostOptions { StorePath = Store });
            store.RecordTrusted(
                Identity(),
                Worktree,
                ExtensionTrustStore.ComputeContentHash(ExtensionDirectory),
                DateTimeOffset.UtcNow
            );
        }

        public List<string> PrintedHashes(string output) =>
            [
                .. output
                    .Split('\n')
                    .Where(line => line.StartsWith("hash: ", StringComparison.Ordinal))
                    .Select(line => line["hash: ".Length..]),
            ];

        public string PrintedDigest()
        {
            (int exitCode, string output, _) = Run();
            Assert.Equal(TrustCommand.ExitPending, exitCode);
            return Value(output, "digest: ");
        }

        private static string Value(string output, string label) =>
            output.Split('\n').Single(line => line.StartsWith(label, StringComparison.Ordinal))[
                label.Length..
            ];

        private static string Normalize(string text) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
