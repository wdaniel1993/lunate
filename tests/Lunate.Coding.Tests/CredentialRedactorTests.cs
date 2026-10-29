using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// Credential redaction (design decision 6): the values Lunate holds are replaced by name, by exact
/// value only, in text and in the session file's bytes.
/// </summary>
public sealed class CredentialRedactorTests
{
    private const string ProviderKey = "sk-ant-abcdef-123456";
    private const string AuthKey = "sk-openai-123456";
    private const string ExtensionSecret = "ext-secret-777";
    private const string McpSecret = "mcp-secret-555";

    [Fact]
    public void Every_value_the_session_knows_is_named_from_its_own_source()
    {
        using var temp = new TempDirectory();
        WriteAuth(temp, AuthKey);
        WriteExtensionSecret(temp, ExtensionSecret);
        string mcp = temp.File("mcp.json");
        File.WriteAllText(
            mcp,
            "{\"mcpServers\":{\"notes\":{\"command\":\"notes\","
                + "\"env\":{\"NOTES_TOKEN\":\""
                + McpSecret
                + "\"}}}}"
        );
        CredentialRedactor redactor = CredentialRedactorSource.Collect(
            AuthStore.Load(temp.File("auth.json"), Environment.Of()),
            Environment.Of(("ANTHROPIC_API_KEY", ProviderKey)),
            temp.Root,
            [mcp]
        );

        string redacted = redactor.Redact(
            $"auth={AuthKey} env={ProviderKey} ext={ExtensionSecret} mcp={McpSecret}"
        );

        Assert.Equal(
            "auth=[redacted:openai] env=[redacted:ANTHROPIC_API_KEY] "
                + "ext=[redacted:demo.token] mcp=[redacted:mcp.notes.NOTES_TOKEN]",
            redacted
        );
        Assert.Equal(4, redactor.Count);
    }

    [Fact]
    public void Only_exact_known_values_are_replaced()
    {
        var redactor = new CredentialRedactor([("openai", AuthKey)]);

        string redacted = redactor.Redact(
            $"known={AuthKey} near={AuthKey}x shape=sk-something-else-entirely"
        );

        Assert.Equal(
            "known=[redacted:openai] near=[redacted:openai]x " + "shape=sk-something-else-entirely",
            redacted
        );
    }

    [Fact]
    public void The_longest_value_is_replaced_first_so_no_secret_survives_as_a_fragment()
    {
        var redactor = new CredentialRedactor([
            ("short", "sk-abcdef"),
            ("long", "sk-abcdef-ghi-jkl"),
        ]);

        Assert.Equal(
            "[redacted:long]",
            redactor.Redact("sk-abcdef-ghi-jkl"),
            StringComparer.Ordinal
        );
    }

    [Fact]
    public void A_value_shorter_than_four_characters_is_skipped()
    {
        var redactor = new CredentialRedactor([("tiny", "ab"), ("key", AuthKey)]);

        string redacted = redactor.Redact("ab and " + AuthKey);

        Assert.Equal("ab and [redacted:key]", redacted);
        Assert.Equal(1, redactor.Count);
    }

    [Fact]
    public void The_jsonl_bytes_carry_the_marker_in_place_of_the_value()
    {
        var redactor = new CredentialRedactor([("openai", AuthKey)]);
        byte[] source = System.Text.Encoding.UTF8.GetBytes(
            "{\"text\":\"the key " + AuthKey + " leaked\"}\n"
        );

        byte[] redacted = redactor.RedactUtf8(source);

        Assert.Equal(
            "{\"text\":\"the key [redacted:openai] leaked\"}\n",
            System.Text.Encoding.UTF8.GetString(redacted)
        );
    }

    [Fact]
    public void A_value_that_names_another_credential_does_not_corrupt_the_marker()
    {
        var redactor = new CredentialRedactor([
            ("MY_API_KEY", "sk-1234567890"),
            ("placeholder", "MY_API_KEY"),
        ]);

        string redacted = redactor.Redact("MY_API_KEY=sk-1234567890");

        Assert.Equal(
            "[redacted:placeholder]=[redacted:MY_API_KEY]",
            redacted,
            StringComparer.Ordinal
        );
    }

    [Fact]
    public void Redacting_an_already_redacted_text_is_identity()
    {
        var redactor = new CredentialRedactor([
            ("KEY_TOKEN", "KEY_TOKEN"),
            ("other", "second-value-9999"),
        ]);
        const string input = "KEY_TOKEN=KEY_TOKEN value=second-value-9999";

        string first = redactor.Redact(input);

        Assert.Equal(
            "[redacted:KEY_TOKEN]=[redacted:KEY_TOKEN] value=[redacted:other]",
            first,
            StringComparer.Ordinal
        );
        Assert.Equal(first, redactor.Redact(first), StringComparer.Ordinal);
    }

    [Fact]
    public void The_byte_level_scan_never_nests_markers()
    {
        var redactor = new CredentialRedactor([
            ("MY_API_KEY", "sk-1234567890"),
            ("placeholder", "MY_API_KEY"),
            ("KEY_TOKEN", "KEY_TOKEN"),
            ("other", "second-value-9999"),
        ]);
        byte[] source = System.Text.Encoding.UTF8.GetBytes(
            "{\"text\":\"MY_API_KEY=sk-1234567890\"}\n"
                + "{\"text\":\"KEY_TOKEN=KEY_TOKEN value=second-value-9999\"}\n"
        );

        byte[] firstBytes = redactor.RedactUtf8(source);
        string first = System.Text.Encoding.UTF8.GetString(firstBytes);
        string second = System.Text.Encoding.UTF8.GetString(redactor.RedactUtf8(firstBytes));

        Assert.Equal(
            "{\"text\":\"[redacted:placeholder]=[redacted:MY_API_KEY]\"}\n"
                + "{\"text\":\"[redacted:KEY_TOKEN]=[redacted:KEY_TOKEN] value=[redacted:other]\"}\n",
            first,
            StringComparer.Ordinal
        );
        Assert.Equal(first, second, StringComparer.Ordinal);
    }

    [Fact]
    public void A_secret_file_that_does_not_parse_contributes_nothing_and_does_not_throw()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "extensions-secrets"));
        File.WriteAllText(Path.Combine(temp.Root, "extensions-secrets", "broken.json"), "{");

        CredentialRedactor redactor = CredentialRedactorSource.Collect(
            AuthStore.Load(temp.File("auth.json"), Environment.Of()),
            Environment.Of(),
            temp.Root,
            []
        );

        Assert.Equal(0, redactor.Count);
    }

    [Fact]
    public void The_provider_table_supplies_the_environment_names()
    {
        var redactor = new CredentialRedactor([("ANTHROPIC_API_KEY", ProviderKey)]);

        Assert.Contains("ANTHROPIC_API_KEY", ProviderPresets.All.Select(p => p.KeyVariable));
        Assert.Equal("[redacted:ANTHROPIC_API_KEY]", redactor.Redact(ProviderKey));
    }

    [Fact]
    public void A_bracketed_known_secret_is_masked_not_passed_through_as_a_marker()
    {
        var redactor = new CredentialRedactor([("OPENAI_API_KEY", "sk-live-9f8e7d6c5b4a")]);
        const string input = "[redacted:sk-live-9f8e7d6c5b4a]";

        string redacted = redactor.Redact(input);

        Assert.DoesNotContain("sk-live-9f8e7d6c5b4a", redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted:OPENAI_API_KEY]", redacted, StringComparison.Ordinal);
        Assert.Equal(redacted, redactor.Redact(redacted), StringComparer.Ordinal);
    }

    [Fact]
    public void A_bracketed_known_secret_is_masked_in_the_jsonl_bytes_too()
    {
        var redactor = new CredentialRedactor([("OPENAI_API_KEY", "sk-live-9f8e7d6c5b4a")]);
        byte[] source = System.Text.Encoding.UTF8.GetBytes(
            "{\"text\":\"[redacted:sk-live-9f8e7d6c5b4a]\"}\n"
        );

        byte[] redacted = redactor.RedactUtf8(source);

        string first = System.Text.Encoding.UTF8.GetString(redacted);
        Assert.Equal(
            "{\"text\":\"[redacted:[redacted:OPENAI_API_KEY]]\"}\n",
            first,
            StringComparer.Ordinal
        );
        Assert.Equal(
            first,
            System.Text.Encoding.UTF8.GetString(redactor.RedactUtf8(redacted)),
            StringComparer.Ordinal
        );
    }

    [Fact]
    public void A_marker_with_an_unknown_name_passes_through_unchanged()
    {
        var redactor = new CredentialRedactor([("openai", AuthKey)]);

        const string input = "password: [redacted:SOMETHING_ELSE] untouched";

        Assert.Equal(input, redactor.Redact(input), StringComparer.Ordinal);
    }

    [Fact]
    public void An_unknown_marker_and_a_known_marker_share_one_text_unchanged()
    {
        var redactor = new CredentialRedactor([("openai", AuthKey)]);

        const string input = "a [redacted:X] b [redacted:openai] c";

        Assert.Equal(input, redactor.Redact(input), StringComparer.Ordinal);
    }

    private static void WriteAuth(TempDirectory temp, string key) =>
        File.WriteAllText(
            temp.File("auth.json"),
            "{\"schemaVersion\":1,\"keys\":{\"openai\":\"" + key + "\"}}"
        );

    private static void WriteExtensionSecret(TempDirectory temp, string secret)
    {
        Directory.CreateDirectory(Path.Combine(temp.Root, "extensions-secrets"));
        File.WriteAllText(
            Path.Combine(temp.Root, "extensions-secrets", "demo.json"),
            "{\"token\":\"" + secret + "\"}"
        );
    }

    private static class Environment
    {
        internal static Func<string, string?> Of(params (string Name, string Value)[] values)
        {
            var map = values.ToDictionary(pair => pair.Name, pair => pair.Value);
            return name => map.GetValueOrDefault(name);
        }

        internal static Func<string, string?> Of() => _ => null;
    }
}
