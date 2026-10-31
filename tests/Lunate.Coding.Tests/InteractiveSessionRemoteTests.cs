using System.Text.Json.Nodes;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive session's remote-extension wiring: a background manifest starts its child only
/// after the UI is ready, and the <c>/extensions restart</c> command answers for remote ids.
/// </summary>
public sealed class InteractiveSessionRemoteTests
{
    [Fact]
    public async Task A_background_remote_extension_starts_after_the_ui_is_ready()
    {
        using var extensionTemp = new TempDirectory();
        string marker = extensionTemp.File("started.marker");
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: BackgroundSeams(extensionTemp, marker, log)
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() =>
            host.Session.IsComposed || host.Diagnostics.Length > 0 || run.IsFaulted
        );
        Assert.False(run.IsFaulted, $"run faulted: {run.Exception}");
        Assert.True(host.Session.IsComposed, $"compose failed: {host.Diagnostics}");
        // The trigger fires after composition (the UI's loading state cleared); the child starts
        // asynchronously, so the marker appears in the spawn window.
        try
        {
            await host.WaitUntilAsync(() => File.Exists(marker), WaitBudgets.SpawnWindow);
        }
        catch (TimeoutException)
        {
            Assert.Fail(
                $"marker missing; log=[{string.Join(" | ", log.Messages)}] diag=[{host.Diagnostics}]"
            );
        }

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_extensions_restart_command_answers_for_unknown_ids()
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        try
        {
            await host.WaitUntilAsync(() => host.Session.IsComposed);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"diagnostics=[{host.Diagnostics}] frames=[{host.Frames}]");
        }

        host.Console.SendText("/extensions restart absent");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "is not a loaded remote extension"
        );

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        // The empty store still composes the builtin, so the listing starts with roslyn.
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "extensions: roslyn");

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static ExtensionSeams BackgroundSeams(
        TempDirectory temp,
        string markerPath,
        RecordingExtensionLog log
    )
    {
        string store = temp.File("store");
        string directory = Path.Combine(store, "extensions", "background-sample");
        Directory.CreateDirectory(directory);

        string script =
            "const readline = require('readline');"
            + "const fs = require('fs');"
            + "fs.writeFileSync(process.argv[1], 'started');"
            + "const rl = readline.createInterface({ input: process.stdin });"
            + "rl.on('line', line => {"
            + "  let frame;"
            + "  try { frame = JSON.parse(line); } catch (error) { return; }"
            + "  if (frame.method === 'initialize') {"
            + "    process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: frame.id,"
            + "      result: { protocolVersion: 1, hooks: [], services: [] } }) + '\\n');"
            + "  } else if (frame.method === 'shutdown') {"
            + "    process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: frame.id,"
            + "      result: null }) + '\\n');"
            + "    setTimeout(() => process.exit(0), 10);"
            + "  }"
            + "});";

        var manifest = new JsonObject
        {
            ["id"] = "background-sample",
            ["version"] = "0.1.0",
            ["apiVersion"] = "^1.0.0",
            ["runtime"] = new JsonObject
            {
                ["command"] = "node",
                ["args"] = new JsonArray("-e", script, markerPath),
                ["startup"] = "background",
            },
        };
        File.WriteAllText(Path.Combine(directory, "extension.json"), manifest.ToJsonString());

        return new ExtensionSeams { StorePath = store, Log = log };
    }
}
