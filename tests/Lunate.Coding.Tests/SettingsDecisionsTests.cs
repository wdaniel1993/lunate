using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>decisions</c> block: one settings block that names the decision profile and
/// configures all four consumers without aliases for anything released or not.
/// </summary>
public sealed class SettingsDecisionsTests
{
    private const string FullBlock = """
        {
          "schemaVersion": 2,
          "profiles": {
            "decision-profile": { "provider": "router", "model": "router/model-a" },
            "routing-one": { "provider": "router", "model": "router/model-a" },
            "routing-two": { "provider": "router", "model": "router/model-b" }
          },
          "decisions": {
            "profile": "decision-profile",
            "approvals": "shadow",
            "approvalsThreshold": 0.75,
            "routing": "enforce",
            "routingProfiles": ["routing-one", "routing-two"],
            "tool": true,
            "questions": "suggest"
          }
        }
        """;

    private static string Json(string decisions) =>
        """{"schemaVersion": 2, """ + decisions.Trim() + "}";

    [Fact]
    public void Decisions_default_to_off_with_a_half_threshold_and_no_profile()
    {
        AgentSettings defaults = SettingsStore.Resolve(null, _ => null);

        Assert.Null(defaults.Decisions.Profile);
        Assert.Equal(DecisionMode.Off, defaults.Decisions.Approvals);
        Assert.Equal(0.5, defaults.Decisions.ApprovalsThreshold);
        Assert.Equal(DecisionMode.Off, defaults.Decisions.Routing);
        Assert.Empty(defaults.Decisions.RoutingProfiles);
        Assert.False(defaults.Decisions.Tool);
        Assert.Equal(DecisionQuestionsMode.Off, defaults.Decisions.Questions);
    }

    [Fact]
    public void The_block_configures_the_profile_and_every_consumer()
    {
        AgentSettings settings = SettingsStore.Resolve(FullBlock, _ => null);

        Assert.Equal("decision-profile", settings.Decisions.Profile);
        Assert.Equal(DecisionMode.Shadow, settings.Decisions.Approvals);
        Assert.Equal(0.75, settings.Decisions.ApprovalsThreshold);
        Assert.Equal(DecisionMode.Enforce, settings.Decisions.Routing);
        Assert.Equal(["routing-one", "routing-two"], settings.Decisions.RoutingProfiles);
        Assert.True(settings.Decisions.Tool);
        Assert.Equal(DecisionQuestionsMode.Suggest, settings.Decisions.Questions);
    }

    [Fact]
    public void Old_flat_keys_are_refused_without_aliases()
    {
        foreach (string bad in ["decisionMode", "routingMode", "routingProfiles"])
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                SettingsStore.Resolve(Json($""" "{bad}": "off" """), _ => null)
            );

            Assert.Contains($"'{bad}'", exception.Message, StringComparison.Ordinal);
            Assert.Contains("unknown key", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Unknown_keys_inside_the_block_name_the_field()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                Json(""" "decisions": { "unknownKey": true } """),
                _ => null
            )
        );

        Assert.Contains("'decisions.unknownKey'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unknown key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_block_must_be_an_object()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(Json(""" "decisions": "off" """), _ => null)
        );

        Assert.Contains(
            "'decisions' must be an object",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Unknown_consumer_modes_are_refused_naming_the_field()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                Json(
                    """ "decisions": { "profile": "decision-profile", "approvals": "always", "routing": "sometimes" } """
                ),
                _ => null
            )
        );

        Assert.Contains("'decisions.approvals'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'decisions.routing'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "must be 'off', 'shadow', or 'enforce'",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("\"high\"")]
    public void An_out_of_range_or_non_number_threshold_is_refused_naming_the_field(string bad)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                Json($$""" "decisions": { "approvalsThreshold": {{bad}} } """),
                _ => null
            )
        );

        Assert.Contains(
            "'decisions.approvalsThreshold' must be a finite number from 0 through 1",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Tool_must_be_a_boolean()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(Json(""" "decisions": { "tool": "on" } """), _ => null)
        );

        Assert.Contains(
            "'decisions.tool' must be true or false",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Questions_accepts_only_off_and_suggest()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                Json(""" "decisions": { "questions": "enforce" } """),
                _ => null
            )
        );

        Assert.Contains("'decisions.questions'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "must be 'off' or 'suggest'",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_profile_must_be_a_non_empty_string()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(Json(""" "decisions": { "profile": "" } """), _ => null)
        );

        Assert.Contains(
            "'decisions.profile' must be a non-empty profile name",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    private const string ProfilesAndUnknownDecisionProfile = """
        {
          "schemaVersion": 2,
          "profiles": {
            "routing-one": { "provider": "router", "model": "router/model-a" },
            "routing-two": { "provider": "router", "model": "router/model-b" }
          },
          "decisions": {
            "profile": "missing-profile",
            "routing": "enforce",
            "routingProfiles": ["routing-one", "missing-also"]
          }
        }
        """;

    [Fact]
    public void Unknown_profile_names_are_refused_naming_the_field_and_collected()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(ProfilesAndUnknownDecisionProfile, _ => null)
        );

        Assert.Contains(
            "'decisions.profile' names an unknown profile",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "'routingProfiles' names unknown profiles",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "'decisions.routing' requires at least two 'routingProfiles'",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Routing_refuses_unknown_profile_names_even_when_off()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                Json(""" "decisions": { "routingProfiles": ["missing"] } """),
                _ => null
            )
        );

        Assert.Contains(
            "'routingProfiles' names unknown profiles",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void More_than_255_routing_profiles_are_refused()
    {
        string entries = string.Join(
            ", ",
            Enumerable.Range(0, 256).Select(i => $"\"routing-{i}\"")
        );
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                Json($$""" "decisions": { "routingProfiles": [{{entries}}] } """),
                _ => null
            )
        );

        Assert.Contains("255", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Consumers_turned_on_without_a_profile_resolve_without_an_error_state()
    {
        AgentSettings settings = SettingsStore.Resolve(
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "routing-one": { "provider": "router", "model": "router/model-a" },
                "routing-two": { "provider": "router", "model": "router/model-b" }
              },
              "decisions": {
                "approvals": "enforce",
                "routing": "enforce",
                "routingProfiles": ["routing-one", "routing-two"],
                "tool": true,
                "questions": "suggest"
              }
            }
            """,
            _ => null
        );

        Assert.Null(settings.Decisions.Profile);
        Assert.Equal(DecisionMode.Enforce, settings.Decisions.Approvals);
        Assert.Equal(DecisionMode.Enforce, settings.Decisions.Routing);
        Assert.True(settings.Decisions.Tool);
        Assert.Equal(DecisionQuestionsMode.Suggest, settings.Decisions.Questions);
    }

    [Fact]
    public void Project_settings_cannot_carry_the_decisions_block()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("project-settings.json"),
            """{ "schemaVersion": 2, "decisions": { "tool": true } }"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.ResolveWithProject(
                null,
                temp.File("project-settings.json"),
                [],
                _ => null
            )
        );

        Assert.Contains("'decisions'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not allowed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Usages_can_not_select_the_decision_profile_any_more()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                Json(""" "usages": { "decision": "decision-profile" } """),
                _ => null
            )
        );

        Assert.Contains(
            "unknown usage 'usages.decision'",
            exception.Message,
            StringComparison.Ordinal
        );
    }
}
