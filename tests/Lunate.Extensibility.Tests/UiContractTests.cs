using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class UiContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Every_ui_dto_round_trips_through_json()
    {
        foreach ((string name, object dto) in AllDtos())
        {
            string json = JsonSerializer.Serialize(dto, dto.GetType(), Options);
            object? roundTripped = JsonSerializer.Deserialize(json, dto.GetType(), Options);

            Assert.True(roundTripped is not null, $"{name} deserialized to null");
            string again = JsonSerializer.Serialize(roundTripped, dto.GetType(), Options);
            Assert.Equal(json, again);
        }
    }

    [Fact]
    public void Every_text_style_round_trips()
    {
        foreach (TextStyle style in Enum.GetValues<TextStyle>())
        {
            var segment = new TextSegment("text", style);
            string json = JsonSerializer.Serialize(segment, Options);
            TextSegment roundTripped = JsonSerializer.Deserialize<TextSegment>(json, Options)!;

            Assert.Equal(segment, roundTripped);
        }
    }

    private static List<(string Name, object Dto)> AllDtos() =>
        [
            ("TextSegment.Plain", new TextSegment("plain text", TextStyle.Plain)),
            ("TextSegment.Dim", new TextSegment("dim text", TextStyle.Dim)),
            ("TextSegment.Accent", new TextSegment("accent text", TextStyle.Accent)),
            ("TextSegment.Warning", new TextSegment("warning text", TextStyle.Warning)),
            ("TextSegment.Error", new TextSegment("error text", TextStyle.Error)),
            (
                "ToolResultView",
                new ToolResultView("read", "call-1", "file contents", IsError: false)
            ),
            ("ToolResultView.Error", new ToolResultView("bash", "call-2", "boom", IsError: true)),
            ("StatusView", new StatusView("gpt-4o-mini", 12400, 128000, "~/dev/lunate", "main")),
            ("StatusView.NoBranch", new StatusView("gpt-4o-mini", 0, 0, "/repo", null)),
        ];
}
