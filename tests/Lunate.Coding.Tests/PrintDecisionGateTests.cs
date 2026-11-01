using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// Print mode's decision wiring (issue #439): <c>decisions.approvals</c> is enforced in the
/// terminal-less run exactly as in the interactive session. With <c>--yolo</c> calls are
/// auto-approved, so a <c>deny</c> recommendation above the threshold is the one gate that still
/// applies and no one is present to ask — the fail-closed denial of the decision-layer spec.
/// </summary>
public sealed class PrintDecisionGateTests
{
    private const string DenyResponse =
        """{"results":{"approval":{"answer":"deny","probabilities":{"approve":0.13,"deny":0.87}}}}""";

    [Fact]
    public async Task An_enforce_deny_above_the_threshold_is_denied_under_yolo_when_no_user_is_present()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "note.txt"), ("content", "hi"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text(DenyResponse), Scripts.Stop())
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        WriteFixture(temp, decisionProvider: true);

        // The regression the issue reported: with --yolo the gate used to be inert and the
        // denied call ran. The discriminator is the file: a denied write never wrote it, and the
        // answered continuation is the deny turn followed by "done", not the deny text itself.
        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(
            PrintModeTestSupport.BaseOptions(temp, new FakeChatClientFactory(client)) with
            {
                Prompt = "Hi",
                Yolo = true,
            }
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output);
        Assert.False(File.Exists(temp.File("note.txt")));
        Assert.Contains(PrintMode.YoloWarning, errors, StringComparison.Ordinal);
        Assert.DoesNotContain("is unavailable", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unresolvable_decision_profile_degrades_to_no_gate_with_a_warning()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "note.txt"), ("content", "hi"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        // No provider store: the configured decision profile cannot resolve its provider.
        WriteFixture(temp, decisionProvider: false);

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(
            PrintModeTestSupport.BaseOptions(temp, new FakeChatClientFactory(client)) with
            {
                Prompt = "Hi",
                Yolo = true,
            }
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output);
        // The gate is off, and the run says so instead of crashing: the approve-everything path
        // is the --yolo default, and the warning names the profile that failed.
        Assert.True(File.Exists(temp.File("note.txt")));
        Assert.Contains(
            "decision profile 'decision-profile' is unavailable; the decision layer is off",
            errors,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_resolved_decision_profile_with_the_tool_enabled_declares_decide()
    {
        using var temp = new TempDirectory();
        string settingsJson =
            """
            {
              "schemaVersion": 2,
              "model": "gpt-4o-mini",
              "profiles": {
                "decision-profile": { "provider": "decision-provider", "model": "decision-model" }
              },
              "decisions": { "profile": "decision-profile", "approvals": "enforce", "tool": "on" }
            }
            """;
        var client = new ScriptedChatClient()
            .Enqueue(Scripts.Text("ok"), Scripts.Stop());
        WriteFixture(temp, decisionProvider: true, settingsJson: settingsJson);

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(
            PrintModeTestSupport.BaseOptions(temp, new FakeChatClientFactory(client)) with
            {
                Prompt = "Hi",
            }
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Contains("decide", client.Requests[0][0].Text, StringComparison.Ordinal);
        Assert.Contains(
            client.RequestOptions[0]!.Tools!,
            declaration => declaration.Name == "decide"
        );
    }

    private static void WriteFixture(
        TempDirectory temp,
        bool decisionProvider,
        string? settingsJson = null
    )
    {
        File.WriteAllText(temp.File("models.json"), ModelsJson());
        File.WriteAllText(
            temp.File("settings.json"),
            settingsJson
                ?? """
                {
                  "schemaVersion": 2,
                  "model": "gpt-4o-mini",
                  "profiles": {
                    "decision-profile": { "provider": "decision-provider", "model": "decision-model" }
                  },
                  "decisions": { "profile": "decision-profile", "approvals": "enforce" }
                }
                """
        );
        if (decisionProvider)
        {
            ProviderStore.Save(
                AuthStore.ProviderStoreFilePath(temp.File("auth.json")),
                [Provider("decision-provider", "openai")]
            );
        }
    }

    private static string ModelsJson() =>
        """
        {
          "schemaVersion": 2,
          "models": [
            { "id": "gpt-4o-mini", "provider": "openai", "contextWindow": 64000,
              "supportsTools": true },
            { "id": "decision-model", "provider": "openai", "contextWindow": 64000,
              "supportsTools": false }
          ]
        }
        """;

    private static ProviderEntry Provider(string name, string kind)
    {
        bool valid = ProviderEntry.TryCreate(
            name,
            kind,
            new Uri("https://provider.example.test/v1"),
            $"auth:{name}",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }
}
