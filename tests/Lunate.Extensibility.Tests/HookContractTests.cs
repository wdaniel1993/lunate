using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class HookContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Every_hook_dto_round_trips_through_json()
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
    public void Unknown_json_fields_are_ignored()
    {
        const string json =
            """{"runId":"r1","callId":"c1","toolName":"read","output":"ok","isError":false,"future":"ignored"}""";

        ToolResultReadyPayload payload = JsonSerializer.Deserialize<ToolResultReadyPayload>(
            json,
            Options
        )!;

        Assert.Equal("r1", payload.RunId);
        Assert.Equal("c1", payload.CallId);
        Assert.Equal("read", payload.ToolName);
        Assert.Equal("ok", payload.Output);
        Assert.False(payload.IsError);
    }

    [Fact]
    public void Json_element_fields_keep_their_content()
    {
        JsonElement arguments = JsonDocument
            .Parse("""{"path":"a.txt","n":1}""")
            .RootElement.Clone();
        var result = new ToolCallingResult.Proceed(arguments);

        string json = JsonSerializer.Serialize(result, Options);
        ToolCallingResult.Proceed roundTripped =
            JsonSerializer.Deserialize<ToolCallingResult.Proceed>(json, Options)!;

        Assert.True(roundTripped.Arguments!.Value.TryGetProperty("path", out JsonElement path));
        Assert.Equal("a.txt", path.GetString());
    }

    private static List<(string Name, object Dto)> AllDtos()
    {
        JsonElement args = JsonDocument.Parse("""{"path":"a.txt"}""").RootElement.Clone();
        JsonElement data = JsonDocument.Parse("""{"diagnostics":[]}""").RootElement.Clone();
        JsonElement entry = JsonDocument.Parse("""{"note":"remember"}""").RootElement.Clone();

        return
        [
            ("ProjectTrustPayload", new ProjectTrustPayload("ext", "/ext", "repo", "/work")),
            ("ProjectTrustResult.Allow", new ProjectTrustResult.Allow()),
            ("ProjectTrustResult.Deny", new ProjectTrustResult.Deny("no")),
            ("SessionStartedPayload", new SessionStartedPayload("/work", "repo")),
            ("SessionEndingPayload", new SessionEndingPayload("/work", "repo")),
            ("InputReceivedPayload", new InputReceivedPayload("hello")),
            ("InputReceivedResult.PassThrough", new InputReceivedResult.PassThrough()),
            ("InputReceivedResult.Transform", new InputReceivedResult.Transform("changed")),
            ("InputReceivedResult.Consume", new InputReceivedResult.Consume()),
            (
                "RunStartingPayload",
                new RunStartingPayload(
                    "r1",
                    [new PromptSection("system", "base") { Source = "ext" }],
                    ["read", "write"]
                )
            ),
            ("RunStartingResult.None", new RunStartingResult.None()),
            (
                "RunStartingResult.Apply",
                new RunStartingResult.Apply(
                    [new PromptSectionEdit("memory", "remember") { Source = "ext" }],
                    ["read"]
                )
            ),
            (
                "ContextBuildingPayload",
                new ContextBuildingPayload(
                    "r1",
                    [new ContextMessage("system", "base") { Source = "ext" }]
                )
            ),
            ("ContextBuildingResult.None", ContextBuildingResult.None),
            (
                "ContextBuildingResult.Added",
                new ContextBuildingResult([
                    new ContextMessage("user", "injected") { Source = "ext" },
                ])
            ),
            (
                "ProviderStreamEventPayload",
                new ProviderStreamEventPayload("r1", "model-1", "text", "stop")
            ),
            ("MessageCompletedPayload", new MessageCompletedPayload("r1", "assistant", "text")),
            ("MessageCompletedResult.Keep", new MessageCompletedResult.Keep()),
            ("MessageCompletedResult.Replace", new MessageCompletedResult.Replace("fixed")),
            ("ToolCallingPayload", new ToolCallingPayload("r1", "c1", "read", args)),
            ("ToolCallingResult.Proceed", new ToolCallingResult.Proceed(null)),
            ("ToolCallingResult.Proceed.Args", new ToolCallingResult.Proceed(args)),
            ("ToolCallingResult.Block", new ToolCallingResult.Block("not allowed")),
            (
                "ToolResultReadyPayload",
                new ToolResultReadyPayload("r1", "c1", "read", "output", false)
            ),
            ("ToolResultReadyResult", new ToolResultReadyResult("output", null)),
            ("ToolResultReadyResult.Data", new ToolResultReadyResult("output", data)),
            ("TurnEndedPayload", new TurnEndedPayload("r1")),
            ("TurnEndedEntry", new TurnEndedEntry("memory", entry) { ExtensionId = "ext" }),
            ("TurnEndedResult.None", new TurnEndedResult.None()),
            (
                "TurnEndedResult.TurnEnded",
                new TurnEndedResult.TurnEnded([new TurnEndedEntry("memory", entry)], true)
            ),
            ("RunSettledPayload", new RunSettledPayload("r1")),
            ("CompactingPayload", new CompactingPayload("r1", [new ContextMessage("user", "old")])),
            ("CompactingResult.UseDefault", new CompactingResult.UseDefault()),
            ("CompactingResult.Provide", new CompactingResult.Provide("summary")),
            ("ModelChangedPayload", new ModelChangedPayload("r1", "model-2")),
            ("ToolsChangedPayload", new ToolsChangedPayload("r1", ["read", "write"])),
            ("FileChangedPayload", new FileChangedPayload("/work/a.txt", "/work")),
            (
                "ModelProviderDescriptor",
                new ModelProviderDescriptor(
                    "acme-local",
                    "Acme Local",
                    "http://localhost:11434/v1",
                    "acme-key",
                    ["acme-7b"]
                )
            ),
        ];
    }
}
