using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The one scrubbing path a host diagnostic takes before it reaches a diagnostic bundle or the
/// console: a fake credential is redacted, a URL loses userinfo and query, the home directory
/// becomes <c>~</c>, the user name leaves paths, and an echo-prone exception reduces to its type
/// and position. The sentinel check runs over the bundle file's bytes.
/// </summary>
public sealed class DiagnosticScrubberTests
{
    private const string Credential = "sk-live-SENTINEL0123456789";
    private const string Url =
        "https://runner:topsecret@collector.internal/v1/notes?api_key=abc123";
    private const string Home = "/Users/spriend";

    private static readonly Func<DateTimeOffset> Clock = () =>
        new DateTimeOffset(2026, 10, 28, 11, 0, 0, TimeSpan.Zero);

    private static DiagnosticScrubber Scrubber() =>
        new(new CredentialRedactor([("sentinel", Credential)]), Home, "spriend");

    [Theory]
    [InlineData(0x80070070, "disk_full")]
    [InlineData(0x80070005, "access_denied")]
    [InlineData(0x80070020, "sharing_violation")]
    [InlineData(0x80070002, "not_found")]
    [InlineData(0x80070003, "not_found")]
    [InlineData(0x800700CE, "path_too_long")]
    [InlineData(0x8007007B, "invalid_name")]
    [InlineData(0x80070027, "handle_disk_full")]
    [InlineData(0x80070050, "0x80070050")]
    public void Category_maps_each_hresult_to_its_token(long hresult, string expected)
    {
        Assert.Equal(
            expected,
            DiagnosticScrubber.Category(unchecked((int)hresult)),
            ignoreCase: false
        );
    }

    [Fact]
    public void Describe_carries_the_hresult_as_code_over_prose()
    {
        var scrubber = new DiagnosticScrubber(CredentialRedactor.Empty, "/Users/tester", "tester");

        string form = scrubber.Describe(
            new IOException("/Users/tester/secrets/auth.json: Access is denied")
        );

        Assert.StartsWith("IOException (0x", form, StringComparison.Ordinal);
        Assert.EndsWith("): ~/secrets/auth.json: Access is denied", form, StringComparison.Ordinal);
        Assert.DoesNotContain("topsecret", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_prints_no_message_part_when_the_message_scrubs_to_nothing()
    {
        var scrubber = new DiagnosticScrubber(CredentialRedactor.Empty, null, null);

        string form = scrubber.Describe(new IOException(""));

        Assert.Equal("IOException (0x80131620)", form, ignoreCase: false);
    }

    [Fact]
    public void Scrub_takes_the_credential_the_url_the_home_and_the_user_name_out_of_a_message()
    {
        string line = Scrubber()
            .Scrub(
                "spill failed: "
                    + Url
                    + " sent /Users/spriend/.lunate/sessions/ses_1/tool-output/run_1-2.txt the key "
                    + Credential
            );

        Assert.Equal(
            "spill failed: https://collector.internal/v1/notes"
                + " sent ~/.lunate/sessions/ses_1/tool-output/run_1-2.txt the key [redacted:sentinel]",
            line,
            ignoreCase: false
        );
    }

    [Fact]
    public void Scrub_removes_a_bare_user_name_segment_inside_a_path()
    {
        Assert.Equal("/home/cache", Scrubber().Scrub("/home/spriend/cache"));
    }

    [Fact]
    public void Scrub_leaves_a_home_that_is_not_followed_by_a_separator()
    {
        var scrubber = new DiagnosticScrubber(CredentialRedactor.Empty, "/Users/tester", "tester");

        Assert.Equal(
            "staging /Users/tester-2/cache is another account",
            scrubber.Scrub("staging /Users/tester-2/cache is another account"),
            ignoreCase: false
        );
    }

    [Fact]
    public void Describe_reduces_a_format_exception_to_its_type()
    {
        var exception = new FormatException("The input 'CORRUPT-ECHO' could not be parsed");
        var scrubber = Scrubber();

        string form = scrubber.Describe(exception);

        Assert.Equal("FormatException", form, ignoreCase: false);
        Assert.DoesNotContain("CORRUPT-ECHO", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_reduces_a_json_exception_to_type_line_byte_and_path()
    {
        var exception = new JsonException("bad payload", "cfg/settings.json", 2, 17);
        var scrubber = Scrubber();

        string form = scrubber.Describe(exception);

        Assert.Equal(
            "JsonException line=2 byte=17 path=cfg/settings.json",
            form,
            ignoreCase: false
        );
        Assert.DoesNotContain("bad payload", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_reduces_an_xml_exception_to_type_line_and_position()
    {
        var exception = new XmlException("bad xml", null, 3, 5);
        var scrubber = Scrubber();

        string form = scrubber.Describe(exception);

        Assert.Equal("XmlException line=3 position=5", form, ignoreCase: false);
        Assert.DoesNotContain("bad xml", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_reduces_a_decoder_fallback_exception_to_type_and_index()
    {
        var exception = new DecoderFallbackException("bad byte", [0xFF], 7);
        var scrubber = Scrubber();

        string form = scrubber.Describe(exception);

        Assert.Equal("DecoderFallbackException index=7", form, ignoreCase: false);
        Assert.DoesNotContain("bad byte", form, StringComparison.Ordinal);
    }

    [Fact]
    public void The_written_bundle_carries_no_credential_no_url_secret_no_user_name_or_echo()
    {
        using var temp = new TempDirectory();
        string destination = temp.File("sentinel-bundle.txt");
        var redactor = new CredentialRedactor([("sentinel", Credential)]);
        var scrubber = new DiagnosticScrubber(redactor, Home, "spriend");
        DateTimeOffset at = Clock();
        DiagnosticEvent[] events =
        [
            new(
                at,
                DiagnosticBundle.Describe(
                    new ToolOutputSpillFailed(
                        "run_1",
                        "spill failed writing /Users/spriend/.lunate/sessions/ses_1/tool-output/run_1-2.txt: The disk is full"
                    ),
                    scrubber
                )
            ),
            new(
                at.AddMilliseconds(1),
                scrubber.Describe(
                    new FormatException("The input 'CORRUPT-ECHO-INPUT' could not be parsed")
                )
            ),
        ];
        var input = new DiagnosticBundleInput
        {
            Destination = destination,
            GeneratedAt = at,
            SessionId = "ses_1",
            StateRoot = temp.File("state"),
            HomeConfigured = false,
            Settings = new AgentSettings(),
            AuthKeys = [],
            Model = new ModelInfo(
                "gpt-4o-mini",
                "openai",
                Endpoint: new Uri("https://api.openai.com/v1"),
                ContextWindow: 128000,
                SupportsTools: true
            ),
            ModelSource = "catalog",
            Thinking = ThinkingLevel.Off,
            ExtensionsText = "extensions: none configured",
            ExtensionErrors =
            [
                new ExtensionSessionError(
                    "acme",
                    "load failed: reading /Users/spriend/.lunate/extensions/acme/config from "
                        + Url
                        + " with key "
                        + Credential
                ),
            ],
            Scrubber = scrubber,
            Events = events,
            RecordedEvents = events.Length,
        };

        DiagnosticBundle.Write(input, redactor);

        string bundle = Encoding.UTF8.GetString(File.ReadAllBytes(destination));
        foreach (
            string seed in (string[])
                [
                    Credential,
                    "runner:topsecret",
                    "topsecret",
                    "api_key=abc123",
                    "spriend",
                    "CORRUPT-ECHO-INPUT",
                    "/Users/",
                    "https://runner",
                ]
        )
        {
            Assert.DoesNotContain(seed, bundle, StringComparison.Ordinal);
        }

        Assert.Contains("[redacted:sentinel]", bundle, StringComparison.Ordinal);
        Assert.Contains(
            "load failed: reading ~/.lunate/extensions/acme/config"
                + " from https://collector.internal/v1/notes with key [redacted:sentinel]",
            bundle,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "message=spill failed writing ~/.lunate/sessions/ses_1/tool-output/run_1-2.txt"
                + ": The disk is full",
            bundle,
            StringComparison.Ordinal
        );
        Assert.Contains("FormatException", bundle, StringComparison.Ordinal);
    }

    [Fact]
    public void The_spill_message_is_truncated_after_the_scrub_not_before()
    {
        const string Host = "collector.internal/v1/notes?api_key=abc123";
        string message = "spill failed: https://" + new string('u', 220) + ":topsecret@" + Host;
        var scrubber = Scrubber();

        string line = DiagnosticBundle.Describe(
            new ToolOutputSpillFailed("run_1", message),
            scrubber
        );

        Assert.Contains(
            " message=spill failed: https://collector.internal/v1/notes",
            line,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("topsecret", line, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('u', 100), line, StringComparison.Ordinal);
    }
}
