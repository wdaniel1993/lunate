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
