using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

public sealed class FixtureFormatTests
{
    private const string FrozenDigest =
        "3cfe520ebfa3ea2a6abaed12037f5cca15301f6b5ba4b9e36e46b82b10b1c9b3";

    // frozen by the image-digest rule
    private const string FrozenImageDigest =
        "c28e50f6f272f23738c43d97f0d33b00bd432d58a2e2e3553617e1faaf7ff1b5";

    [Fact]
    public void Header_line_carries_type_schema_model_and_timestamp()
    {
        DateTimeOffset recordedAt = new(2026, 10, 5, 12, 34, 56, TimeSpan.Zero);

        string line = FixtureFormat.SerializeHeader("gpt-4o-mini", recordedAt);

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        Assert.Equal(
            new[] { "type", "schema", "model", "recordedAt" },
            root.EnumerateObject().Select(property => property.Name)
        );
        Assert.Equal("header", root.GetProperty("type").GetString());
        Assert.Equal(FixtureFormat.SchemaVersion, root.GetProperty("schema").GetInt32());
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());
        Assert.Equal(recordedAt, root.GetProperty("recordedAt").GetDateTimeOffset());
    }

    [Fact]
    public void Exchange_line_round_trips_rich_updates_byte_identically()
    {
        List<ChatResponseUpdate> updates =
        [
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new TextContent("Hello "),
                    new FunctionCallContent(
                        "call-1",
                        "read",
                        new Dictionary<string, object?> { ["path"] = "a.txt" }
                    ),
                ]
            )
            {
                ModelId = "gpt-4o-mini",
                MessageId = "msg-1",
                ResponseId = "resp-1",
            },
            new ChatResponseUpdate(
                ChatRole.Tool,
                [new FunctionResultContent("call-1", "file contents")]
            )
            {
                ModelId = "gpt-4o-mini",
            },
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2 })]
            )
            {
                ModelId = "gpt-4o-mini",
                FinishReason = ChatFinishReason.Stop,
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new ErrorContent("provider hiccup")])
            {
                ModelId = "gpt-4o-mini",
            },
        ];

        string line = FixtureFormat.SerializeExchange("deadbeef", updates);
        FixtureExchange exchange = FixtureFormat.ParseExchange(line, "fixture.jsonl", 2);

        Assert.Equal("deadbeef", exchange.RequestDigest);
        Assert.Equal(updates.Count, exchange.Updates.Count);
        Assert.Equal("Hello ", exchange.Updates[0].Text);
        Assert.IsType<FunctionCallContent>(exchange.Updates[0].Contents[1]);
        Assert.Equal(
            line,
            FixtureFormat.SerializeExchange(exchange.RequestDigest, exchange.Updates)
        );
    }

    [Fact]
    public void The_serializer_is_pinned_to_LF_and_re_serializing_a_line_is_byte_identical()
    {
        Assert.Equal("\n", FixtureFormat.JsonOptions.NewLine);

        string header = FixtureFormat.SerializeHeader(
            "gpt-4o-mini",
            new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero)
        );
        string line = FixtureFormat.SerializeExchange(
            "deadbeef",
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a\r\nb")])]
        );

        Assert.DoesNotContain('\r', header);
        Assert.DoesNotContain('\r', line);
        FixtureExchange parsed = FixtureFormat.ParseExchange(line, "fixture.jsonl", 2);
        Assert.Equal(line, FixtureFormat.SerializeExchange(parsed.RequestDigest, parsed.Updates));
    }

    [Fact]
    public void ReadFile_parses_header_and_exchanges_in_order()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        DateTimeOffset recordedAt = new(2026, 10, 5, 12, 34, 56, TimeSpan.Zero);
        string first = FixtureFormat.SerializeExchange(
            "digest-1",
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("one")])]
        );
        string second = FixtureFormat.SerializeExchange(
            "digest-2",
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("two")])]
        );
        File.WriteAllText(
            path,
            string.Join(
                "\n",
                FixtureFormat.SerializeHeader("gpt-4o-mini", recordedAt),
                first,
                second
            ) + "\n"
        );

        FixtureDocument document = FixtureFormat.ReadFile(path);

        Assert.Equal(1, document.Header.Schema);
        Assert.Equal("gpt-4o-mini", document.Header.Model);
        Assert.Equal(
            recordedAt,
            DateTimeOffset.Parse(
                document.Header.RecordedAt,
                System.Globalization.CultureInfo.InvariantCulture
            )
        );
        Assert.Equal(
            new[] { "digest-1", "digest-2" },
            document.Exchanges.Select(exchange => exchange.RequestDigest)
        );
        Assert.Equal("one", document.Exchanges[0].Updates[0].Text);
        Assert.Equal("two", document.Exchanges[1].Updates[0].Text);
    }

    [Fact]
    public void ReadFile_rejects_a_file_without_a_header_naming_the_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        File.WriteAllText(path, "{\"type\":\"exchange\",\"requestDigest\":\"x\",\"updates\":[]}\n");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FixtureFormat.ReadFile(path)
        );

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("header", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFile_rejects_an_unsupported_schema_naming_the_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        File.WriteAllText(
            path,
            "{\"type\":\"header\",\"schema\":2,\"model\":\"m\",\"recordedAt\":\"2026-10-05T00:00:00+00:00\"}\n"
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FixtureFormat.ReadFile(path)
        );

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("2", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            FixtureFormat.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ReadFile_rejects_an_unknown_line_type_naming_the_file_and_line()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        File.WriteAllText(
            path,
            "{\"type\":\"header\",\"schema\":1,\"model\":\"m\",\"recordedAt\":\"2026-10-05T00:00:00+00:00\"}\n{\"type\":\"mystery\"}\n"
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FixtureFormat.ReadFile(path)
        );

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("line 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mystery", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_digest_is_stable_for_identical_requests()
    {
        string first = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            Options()
        );
        string second = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            Options()
        );

        Assert.Equal(first, second);
    }

    [Fact]
    public void Request_digest_matches_the_frozen_schema_value()
    {
        string digest = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            Options()
        );

        Assert.Equal(FrozenDigest, digest);
    }

    [Fact]
    public void Request_digest_changes_when_a_message_changes()
    {
        string baseline = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            Options()
        );
        string changed = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "goodbye")],
            Options()
        );

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void Request_digest_changes_when_the_model_id_changes()
    {
        string baseline = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            Options()
        );
        string changed = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { ModelId = "gpt-4o", Tools = [new NamedTool("read")] }
        );

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void Request_digest_changes_when_tool_names_or_their_order_change()
    {
        string baseline = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { ModelId = "gpt-4o-mini", Tools = [new NamedTool("read")] }
        );
        string renamed = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { ModelId = "gpt-4o-mini", Tools = [new NamedTool("write")] }
        );
        string reordered = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                ModelId = "gpt-4o-mini",
                Tools = [new NamedTool("write"), new NamedTool("read")],
            }
        );

        Assert.NotEqual(baseline, renamed);
        Assert.NotEqual(baseline, reordered);
    }

    [Fact]
    public void Image_digest_input_names_media_type_and_sha256_instead_of_base64()
    {
        byte[] bytes = ImageBytes();

        string input = FixtureFormat.ComputeDigestInput(
            [
                new ChatMessage(
                    ChatRole.User,
                    [new TextContent("look"), new DataContent(bytes, "image/png")]
                ),
            ],
            Options()
        );

        Assert.Contains("\"$type\":\"imageDigest\"", input, StringComparison.Ordinal);
        Assert.Contains(
            $"\"digest\":\"{Convert.ToHexStringLower(SHA256.HashData(bytes))}\"",
            input,
            StringComparison.Ordinal
        );
        Assert.Contains("\"mediaType\":\"image/png\"", input, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(bytes), input, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/png;base64", input, StringComparison.Ordinal);
    }

    [Fact]
    public void A_data_content_with_a_non_image_media_type_is_digested_with_bytes_never_base64()
    {
        byte[] bytes = ImageBytes();
        var content = new DataContent(bytes, "application/octet-stream");

        string input = FixtureFormat.ComputeDigestInput(
            [new ChatMessage(ChatRole.User, [content])],
            Options()
        );

        Assert.Contains(
            "\"mediaType\":\"application/octet-stream\"",
            input,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"\"digest\":\"{Convert.ToHexStringLower(SHA256.HashData(bytes))}\"",
            input,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(Convert.ToBase64String(bytes), input, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_digest_ignores_the_encoding_and_name_of_identical_bytes()
    {
        byte[] bytes = ImageBytes();
        var fromBytes = new DataContent(bytes, "image/png") { Name = "first.png" };
        var fromDataUri = new DataContent(
            $"data:image/png;base64,{Convert.ToBase64String(bytes)}",
            "image/png"
        )
        {
            Name = "second.png",
        };

        string first = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, [fromBytes])],
            Options()
        );
        string second = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, [fromDataUri])],
            Options()
        );

        Assert.Equal(first, second);
    }

    [Fact]
    public void Image_digest_changes_when_one_byte_changes()
    {
        byte[] original = ImageBytes();
        byte[] changed = [.. original];
        changed[^1] ^= 0xFF;

        string baseline = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, [new DataContent(original, "image/png")])],
            Options()
        );
        string flipped = FixtureFormat.ComputeRequestDigest(
            [new ChatMessage(ChatRole.User, [new DataContent(changed, "image/png")])],
            Options()
        );

        Assert.NotEqual(baseline, flipped);
    }

    [Fact]
    public void Image_request_digest_matches_the_frozen_schema_value()
    {
        string digest = FixtureFormat.ComputeRequestDigest(
            [
                new ChatMessage(
                    ChatRole.User,
                    [new TextContent("look"), new DataContent(ImageBytes(), "image/png")]
                ),
            ],
            Options()
        );

        Assert.Equal(FrozenImageDigest, digest);
    }

    [Fact]
    public void Request_digest_is_the_sha256_of_the_digest_input()
    {
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User, [new DataContent(ImageBytes(), "image/png")]),
        ];

        string input = FixtureFormat.ComputeDigestInput(messages, Options());

        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input))),
            FixtureFormat.ComputeRequestDigest(messages, Options())
        );
    }

    private static byte[] ImageBytes() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static ChatOptions Options() =>
        new() { ModelId = "gpt-4o-mini", Tools = [new NamedTool("read")] };
}
