using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class PrintModeTests
{
    [Fact]
    public async Task A_text_only_run_writes_the_answer_to_stdout_and_exits_zero()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(
            Scripts.Text("Hello "),
            Scripts.Text("world"),
            Scripts.Stop()
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("Hello world", output);
        Assert.Empty(errors);
        string sessionId = PrintModeTestSupport.SingleSessionId(temp);
        Assert.Matches("^s_[0-9]{8}-[0-9]{6}-[0-9a-f]{4}$", sessionId);
        string header = File.ReadLines(Path.Combine(temp.File("sessions"), sessionId + ".jsonl"))
            .First();
        Assert.Contains($"\"id\":\"{sessionId}\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_last_assistant_message_reaches_stdout()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("note.txt"), "note");
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Text("Reading. "),
                Scripts.Call("call-1", "read", Scripts.Args(("path", "note.txt"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("Second."), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        // "Last assistant message wins": not "Reading. " (first), not
        // "Reading. Second." (joined), not "Second." (last fragment only).
        Assert.Equal(0, exitCode);
        Assert.Equal("Second.", output);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task A_run_that_reports_cache_usage_writes_only_the_answer_to_stdout()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new UsageContent(
                        new UsageDetails
                        {
                            InputTokenCount = 1500,
                            OutputTokenCount = 50,
                            TotalTokenCount = 1550,
                            CachedInputTokenCount = 1200,
                            AdditionalCounts = new AdditionalPropertiesDictionary<long>
                            {
                                ["CacheCreationInputTokens"] = 300,
                            },
                        }
                    ),
                ]
            ),
            Scripts.Text("Hello"),
            Scripts.Stop()
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("Hello", output);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Json_mode_streams_every_event_in_arrival_order_with_the_session_id()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Text("Checking. "),
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "note.txt"), ("content", "hi"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("Done."), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Write a note.",
            Json = true,
            Yolo = true,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        List<JsonElement> lines = PrintModeTestSupport.ParseLines(output);
        Assert.Equal(
            [
                "run_started",
                "text_message_start",
                "text_message_content",
                "text_message_end",
                "tool_call_start",
                "tool_call_args",
                "tool_call_end",
                "tool_call_result",
                "text_message_start",
                "text_message_content",
                "text_message_end",
                "run_finished",
            ],
            lines.Select(PrintModeTestSupport.TypeOf)
        );
        string sessionId = PrintModeTestSupport.SingleSessionId(temp);
        Assert.All(
            lines,
            line => Assert.Equal(sessionId, line.GetProperty("sessionId").GetString())
        );
        JsonElement result = Assert.Single(
            lines,
            line => PrintModeTestSupport.TypeOf(line) == "tool_call_result"
        );
        Assert.True(result.GetProperty("details").GetProperty("created").GetBoolean());
        Assert.Equal("hi", File.ReadAllText(temp.File("note.txt")));
    }

    [Fact]
    public async Task Json_mode_streams_reasoning_events_with_the_stamped_fields()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(
            Scripts.Reasoning("Think "),
            Scripts.Reasoning("one"),
            Scripts.Text("Answer"),
            Scripts.Stop()
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Json = true,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        List<JsonElement> lines = PrintModeTestSupport.ParseLines(output);
        Assert.Equal(
            [
                "run_started",
                "reasoning_start",
                "reasoning_message_content",
                "reasoning_message_content",
                "reasoning_end",
                "text_message_start",
                "text_message_content",
                "text_message_end",
                "run_finished",
            ],
            lines.Select(PrintModeTestSupport.TypeOf)
        );
        string sessionId = PrintModeTestSupport.SingleSessionId(temp);
        Assert.All(
            lines,
            line => Assert.Equal(sessionId, line.GetProperty("sessionId").GetString())
        );
        JsonElement start = lines[1];
        string messageId = start.GetProperty("messageId").GetString()!;
        Assert.False(string.IsNullOrEmpty(start.GetProperty("runId").GetString()));
        Assert.Equal("Think ", lines[2].GetProperty("text").GetString());
        Assert.Equal("one", lines[3].GetProperty("text").GetString());
        Assert.Equal(messageId, lines[2].GetProperty("messageId").GetString());
        Assert.Equal(messageId, lines[3].GetProperty("messageId").GetString());
        Assert.Equal(messageId, lines[4].GetProperty("messageId").GetString());
    }

    [Fact]
    public async Task No_session_writes_no_session_file()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { NoSession = true },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        string sessions = temp.File("sessions");
        Assert.Empty(Directory.Exists(sessions) ? Directory.GetFiles(sessions, "*.jsonl") : []);
    }

    [Fact]
    public async Task Session_flag_loads_a_session_by_id()
    {
        using var temp = new TempDirectory();
        string sessions = temp.File("sessions");
        Session seeded = SeedSession(
            sessions,
            new Workspace(temp.Root).WorktreeRoot,
            "remembered question",
            DateTime.UtcNow
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Session = seeded.SessionId },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Contains(client.Requests[0], message => message.Text == "remembered question");
        Assert.Equal(seeded.Path, Assert.Single(Directory.GetFiles(sessions, "*.jsonl")));
    }

    [Fact]
    public async Task Session_flag_loads_a_session_by_path()
    {
        using var temp = new TempDirectory();
        string sessions = temp.File("sessions");
        Session seeded = SeedSession(
            sessions,
            new Workspace(temp.Root).WorktreeRoot,
            "path question",
            DateTime.UtcNow
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Session = seeded.Path },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Contains(client.Requests[0], message => message.Text == "path question");
        Assert.Equal(seeded.Path, Assert.Single(Directory.GetFiles(sessions, "*.jsonl")));
    }

    [Fact]
    public async Task Session_flag_with_an_unknown_id_fails_naming_it()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Session = "missing-id" },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "lunate: no session with id 'missing-id'; run a prompt to start one",
            errors,
            StringComparison.Ordinal
        );
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Session_flag_with_a_missing_path_fails_naming_it()
    {
        using var temp = new TempDirectory();
        string missing = temp.File("missing-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Session = missing },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains($"could not load session '{missing}'", errors, StringComparison.Ordinal);
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Ambiguous_session_id_names_the_matching_paths()
    {
        using var temp = new TempDirectory();
        string sessions = temp.File("sessions");
        Session seeded = SeedSession(
            sessions,
            new Workspace(temp.Root).WorktreeRoot,
            "remembered question",
            DateTime.UtcNow
        );
        string duplicate = Path.Combine(
            sessions,
            "duplicate-" + Guid.NewGuid().ToString("N") + ".jsonl"
        );
        File.Copy(seeded.Path, duplicate);
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Session = seeded.SessionId },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            $"session id '{seeded.SessionId}' is ambiguous:",
            errors,
            StringComparison.Ordinal
        );
        Assert.Contains(seeded.Path, errors, StringComparison.Ordinal);
        Assert.Contains(duplicate, errors, StringComparison.Ordinal);
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Continue_loads_the_newest_session_recorded_for_the_working_directory()
    {
        using var temp = new TempDirectory();
        string cwd = new Workspace(temp.Root).WorktreeRoot;
        string sessions = temp.File("sessions");
        SeedSession(
            sessions,
            cwd,
            "older question",
            new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc)
        );
        SeedSession(
            sessions,
            cwd,
            "newest question",
            new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)
        );
        SeedSession(
            sessions,
            temp.File("elsewhere"),
            "other directory",
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Continue = true },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Contains(client.Requests[0], message => message.Text == "newest question");
        Assert.DoesNotContain(client.Requests[0], message => message.Text == "older question");
        Assert.DoesNotContain(client.Requests[0], message => message.Text == "other directory");
    }

    [Fact]
    public async Task Continue_without_a_matching_session_names_the_working_directory()
    {
        using var temp = new TempDirectory();
        string cwd = new Workspace(temp.Root).WorktreeRoot;
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Continue = true },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            $"lunate: no session for '{cwd}'; run a prompt to start one",
            errors,
            StringComparison.Ordinal
        );
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Ask_denies_a_write_with_an_error_result_and_a_stderr_line()
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
            .Enqueue(Scripts.Text("Cannot."), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Write a note.",
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("Cannot.", output);
        Assert.Equal(
            "tool 'write' denied: approval policy 'ask' (pass --yolo to run unattended)\n",
            errors
        );
        Assert.False(File.Exists(temp.File("note.txt")));
    }

    [Fact]
    public async Task Auto_edit_allows_a_write_and_denies_a_command()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{ "schemaVersion": 1, "approval": "auto-edit" }"""
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "note.txt"), ("content", "hi"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(
                Scripts.Call("call-2", "bash", Scripts.Args(("command", "echo hi"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("Done."), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Write and run.",
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("Done.", output);
        Assert.Equal(
            "tool 'bash' denied: approval policy 'auto-edit' (pass --yolo to run unattended)\n",
            errors
        );
        Assert.Equal("hi", File.ReadAllText(temp.File("note.txt")));
    }

    [Fact]
    public async Task Yolo_allows_a_write_and_a_command_without_denials()
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
            .Enqueue(
                Scripts.Call("call-2", "bash", Scripts.Args(("command", "echo hi"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("Done."), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Write and run.",
            Yolo = true,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("Done.", output);
        Assert.Empty(errors);
        Assert.Equal("hi", File.ReadAllText(temp.File("note.txt")));
    }

    [Fact]
    public async Task A_step_limit_run_exits_two_and_streams_the_limit_event()
    {
        using var temp = new TempDirectory();
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(new LoopingChatClient())
        ) with
        {
            Prompt = "Loop forever.",
            Json = true,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        string[] diagnostics = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(51, diagnostics.Length);
        Assert.All(
            diagnostics[..^1],
            line => Assert.Equal("tool 'read' failed: file not found: missing.txt", line)
        );
        Assert.Equal("lunate: step limit reached (50 steps)", diagnostics[^1]);
        List<JsonElement> lines = PrintModeTestSupport.ParseLines(output);
        Assert.Contains(lines, line => PrintModeTestSupport.TypeOf(line) == "step_limit_reached");
        JsonElement last = lines[^1];
        Assert.Equal("run_finished", PrintModeTestSupport.TypeOf(last));
        Assert.Equal("step_limit", last.GetProperty("stopReason").GetString());
    }

    [Fact]
    public async Task Cancellation_exits_130()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        var client = new CancellingChatClient(cancellation);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
        };
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = await PrintMode.RunAsync(options, output, errors, cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Empty(output.ToString());
        Assert.Empty(errors.ToString());
    }

    [Fact]
    public async Task Environment_model_wins_over_the_settings_file()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{ "schemaVersion": 1, "model": "claude-sonnet-5-5" }"""
        );
        var factory = new FakeChatClientFactory(
            new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop())
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal(PrintModeTestSupport.ModelId, factory.CreatedModel!.Id);
    }

    [Fact]
    public async Task A_launch_model_wins_over_the_resolved_settings()
    {
        // The settings file names one model and LUNATE_MODEL another; the launch flag wins over both.
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{ "schemaVersion": 1, "model": "claude-sonnet-5-5" }"""
        );
        var factory = new FakeChatClientFactory(
            new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop())
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Model = "gpt-4o" },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Equal("gpt-4o", factory.CreatedModel!.Id);
    }

    [Fact]
    public async Task A_launch_thinking_level_turns_thinking_on_over_the_default()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 1,
              "models": [
                { "id": "effort-model", "provider": "openai", "endpoint": null, "contextWindow": 128000, "supportsTools": true, "reasoning": "effort" }
              ]
            }
            """
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Environment = PrintModeTestSupport.Environment(model: "effort-model"),
            Launch = new LaunchOptions { Thinking = ThinkingLevel.High },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Equal(ReasoningEffort.High, Assert.Single(client.RequestOptions)?.Reasoning?.Effort);
    }

    [Fact]
    public async Task A_launch_thinking_level_wins_over_the_settings_file()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{ "schemaVersion": 1, "thinking": "low" }"""
        );
        File.WriteAllText(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 1,
              "models": [
                { "id": "effort-model", "provider": "openai", "endpoint": null, "contextWindow": 128000, "supportsTools": true, "reasoning": "effort" }
              ]
            }
            """
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Environment = PrintModeTestSupport.Environment(model: "effort-model"),
            Launch = new LaunchOptions { Thinking = ThinkingLevel.High },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Equal(ReasoningEffort.High, Assert.Single(client.RequestOptions)?.Reasoning?.Effort);
    }

    [Fact]
    public async Task A_launch_model_not_in_the_catalog_fails_naming_it()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Model = "nope-9000" },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "Model 'nope-9000' is not in the catalog",
            errors,
            StringComparison.Ordinal
        );
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task An_ambiguous_launch_model_is_a_usage_error_naming_both_candidates()
    {
        using var temp = new TempDirectory();
        WriteAmbiguousCatalog(temp);
        var factory = new FakeChatClientFactory(
            new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop())
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Environment = PrintModeTestSupport.Environment(model: null),
            Launch = new LaunchOptions { Model = "shared-wire-id" },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains("ambiguous", errors, StringComparison.Ordinal);
        Assert.Contains("openai/shared-model", errors, StringComparison.Ordinal);
        Assert.Contains("groq/shared-model", errors, StringComparison.Ordinal);
        Assert.Null(factory.CreatedModel);
    }

    [Fact]
    public async Task An_ambiguous_configured_model_names_both_candidates_and_exits_one()
    {
        using var temp = new TempDirectory();
        WriteAmbiguousCatalog(temp);
        File.WriteAllText(
            temp.File("settings.json"),
            """{ "schemaVersion": 2, "model": "shared-wire-id" }"""
        );
        var factory = new FakeChatClientFactory(
            new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop())
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Environment = PrintModeTestSupport.Environment(model: null),
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("ambiguous", errors, StringComparison.Ordinal);
        Assert.Contains("openai/shared-model", errors, StringComparison.Ordinal);
        Assert.Contains("groq/shared-model", errors, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", errors, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", errors, StringComparison.Ordinal);
        Assert.Null(factory.CreatedModel);
    }

    [Fact]
    public async Task A_missing_model_is_an_actionable_error()
    {
        using var temp = new TempDirectory();
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(
                new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop())
            )
        ) with
        {
            Prompt = "Hi",
            Environment = PrintModeTestSupport.Environment(model: null),
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("settings.json", errors, StringComparison.Ordinal);
        Assert.Contains("LUNATE_MODEL", errors, StringComparison.Ordinal);
        Assert.Contains("--discover", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_model_points_at_discover()
    {
        using var temp = new TempDirectory();
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(
                new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop())
            )
        ) with
        {
            Prompt = "Hi",
            Environment = PrintModeTestSupport.Environment(model: "nope-9000"),
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("nope-9000", errors, StringComparison.Ordinal);
        Assert.Contains("--discover", errors, StringComparison.Ordinal);
    }

    private static void WriteAmbiguousCatalog(TempDirectory temp) =>
        File.WriteAllText(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "openai/shared-model", "provider": "openai", "wireId": "shared-wire-id" },
                { "id": "groq/shared-model", "provider": "groq", "wireId": "shared-wire-id" }
              ]
            }
            """
        );

    private static Session SeedSession(string root, string cwd, string text, DateTime modified)
    {
        Directory.CreateDirectory(root);
        string provisional = Path.Combine(root, Path.GetRandomFileName());
        Session created = Session.Create(provisional, cwd, null, cwd);
        created.AppendMessage(new ChatMessage(ChatRole.User, text));
        string path = Path.Combine(root, SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        File.SetLastWriteTimeUtc(path, modified);
        return Session.Load(path);
    }
}
