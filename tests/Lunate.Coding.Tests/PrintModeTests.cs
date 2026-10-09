using System.Text.Json;

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
        Assert.Equal(50, diagnostics.Length);
        Assert.All(
            diagnostics,
            line => Assert.Equal("tool 'read' failed: file not found: missing.txt", line)
        );
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
}
