using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class SettingsProfileCapabilitiesTests
{
    [Fact]
    public void User_profile_can_override_open_capability_categories()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "decision": {
                  "provider": "router",
                  "model": "router/model-x",
                  "capabilities": {
                    "apis": ["decision", "future-api"],
                    "features": ["structured-output"]
                  }
                }
              }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        ModelCapabilityDescriptor capabilities = settings
            .Profiles["decision"]
            .CapabilitiesOverride!;
        Assert.Contains("future-api", capabilities.Apis!);
        Assert.Contains("structured-output", capabilities.Features!);
        Assert.Null(capabilities.InputModalities);
        Assert.Null(capabilities.OutputModalities);
    }

    [Fact]
    public void Project_profile_cannot_override_model_capabilities()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("user-settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "decision": { "provider": "router", "model": "router/model-x" }
              }
            }
            """
        );
        File.WriteAllText(
            temp.File("project-settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "decision": {
                  "provider": "router",
                  "model": "router/model-x",
                  "capabilities": { "inputModalities": ["image"] }
                }
              }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.ResolveWithProject(
                temp.File("user-settings.json"),
                temp.File("project-settings.json"),
                [Provider("router")],
                _ => null
            )
        );

        Assert.Contains(
            "profiles.decision.capabilities",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("user-only", exception.Message, StringComparison.Ordinal);
    }

    private static ProviderEntry Provider(string name)
    {
        bool valid = ProviderEntry.TryCreate(
            name,
            "openrouter",
            new Uri("https://openrouter.example.test/v1"),
            "auth:openrouter",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }
}
