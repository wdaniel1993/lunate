using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class McpContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Every_mcp_dto_round_trips_through_json()
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
    public void Every_exposure_value_round_trips()
    {
        foreach (ToolExposure exposure in Enum.GetValues<ToolExposure>())
        {
            string json = JsonSerializer.Serialize(exposure, Options);
            ToolExposure roundTripped = JsonSerializer.Deserialize<ToolExposure>(json, Options);

            Assert.Equal(exposure, roundTripped);
        }
    }

    [Fact]
    public void The_contract_exposure_mirrors_the_agent_exposure_exactly() =>
        Assert.Equal(Enum.GetNames<Lunate.Agent.ToolExposure>(), Enum.GetNames<ToolExposure>());

    private static List<(string Name, object Dto)> AllDtos() =>
        [
            ("McpServerConfig.Minimal", new McpServerConfig("dotnet", ["exec", "server.dll"])),
            (
                "McpServerConfig.Full",
                new McpServerConfig(
                    "dotnet",
                    ["exec", "server.dll", "--flag"],
                    new Dictionary<string, string>
                    {
                        ["LUNATE_TEST_MCP_MARKER"] = "/tmp/marker.txt",
                    },
                    WorkingDirectory: "/tmp/work",
                    CallTimeout: TimeSpan.FromSeconds(30)
                )
            ),
            (
                "McpServerRegistration.Defaults",
                new McpServerRegistration("test", new McpServerConfig("dotnet", ["exec"]))
            ),
            (
                "McpServerRegistration.Overrides",
                new McpServerRegistration(
                    "test",
                    new McpServerConfig("dotnet", ["exec"]),
                    ToolExposure.Programmatic,
                    new Dictionary<string, ToolExposure> { ["echo"] = ToolExposure.Hidden }
                )
            ),
        ];
}
