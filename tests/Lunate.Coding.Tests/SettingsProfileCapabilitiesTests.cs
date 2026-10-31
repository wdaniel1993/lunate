using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class SettingsProfileCapabilitiesTests
{
    [Fact]
    public void Decision_consumer_is_off_by_default_and_user_can_opt_into_enforcement()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            { "schemaVersion": 2, "decisionMode": "enforce" }
            """
        );

        AgentSettings defaults = SettingsStore.Resolve(null, _ => null);
        AgentSettings configured = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(DecisionMode.Off, defaults.DecisionMode);
        Assert.Equal(DecisionMode.Enforce, configured.DecisionMode);
    }

    [Fact]
    public void Project_settings_cannot_enable_decision_consumer()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("project-settings.json"),
            """
            { "schemaVersion": 2, "decisionMode": "enforce" }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.ResolveWithProject(
                null,
                temp.File("project-settings.json"),
                [],
                _ => null
            )
        );

        Assert.Contains("decisionMode", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not allowed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_routing_is_opt_in_and_uses_only_named_profiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "routingMode": "shadow",
              "routingProfiles": ["default", "fast"],
              "profiles": {
                "default": { "provider": "router", "model": "router/model-a" },
                "fast": { "provider": "router", "model": "router/model-b" }
              }
            }
            """
        );

        AgentSettings defaults = SettingsStore.Resolve(null, _ => null);
        AgentSettings configured = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(DecisionMode.Off, defaults.RoutingMode);
        Assert.Equal(DecisionMode.Shadow, configured.RoutingMode);
        Assert.Equal(["default", "fast"], configured.RoutingProfiles);
    }

    [Fact]
    public void Model_routing_requires_two_existing_profiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "routingMode": "enforce",
              "routingProfiles": ["missing"]
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("routingProfiles", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unknown profiles", exception.Message, StringComparison.Ordinal);
        Assert.Contains("at least two", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_settings_cannot_enable_model_routing()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("project-settings.json"),
            """
            { "schemaVersion": 2, "routingMode": "enforce" }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.ResolveWithProject(
                null,
                temp.File("project-settings.json"),
                [],
                _ => null
            )
        );

        Assert.Contains("routingMode", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not allowed", exception.Message, StringComparison.Ordinal);
    }

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
