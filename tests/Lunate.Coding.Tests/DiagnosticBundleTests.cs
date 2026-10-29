using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>/debug</c> through the real host: one file under the state root, the evidence a bug report
/// needs (version, configuration, model, extension graph with digests, state-root paths and the
/// recent events with their timings), no credential values, and no session write.
/// </summary>
public sealed class DiagnosticBundleTests
{
    private static readonly Func<DateTimeOffset> Clock = () =>
        new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Debug_writes_one_bundle_under_the_state_root_and_names_it()
    {
        using var host = new InteractiveSessionHost(clock: Clock);
        host.Console.Size = new ConsoleSize(200, 24);
        host.Scrollback.Profile.Width = 400;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi there");

        await host.SendLineAsync("/debug");
        string notice = await host.WaitForScrollbackAsync("diagnostic bundle written to ");

        string directory = Path.Combine(host.Temp.File("home"), ".lunate", "diagnostics");
        string file = Assert.Single(Directory.GetFiles(directory, "*.txt"));
        Assert.Contains($"diagnostic bundle written to {file}", notice, StringComparison.Ordinal);
        Assert.Contains("review before sharing", notice, StringComparison.Ordinal);

        string bundle = File.ReadAllText(file);
        Assert.StartsWith("lunate diagnostic bundle\n", bundle, StringComparison.Ordinal);
        Assert.Contains(
            "version: " + ProductVersion.Informational,
            bundle,
            StringComparison.Ordinal
        );
        Assert.Contains("platform: ", bundle, StringComparison.Ordinal);
        Assert.Contains("== session ==", bundle, StringComparison.Ordinal);
        Assert.Contains("== state root ==", bundle, StringComparison.Ordinal);
        Assert.Contains(
            "root: " + Path.Combine(host.Temp.File("home"), ".lunate"),
            bundle,
            StringComparison.Ordinal
        );
        Assert.Contains("== config ==", bundle, StringComparison.Ordinal);
        Assert.Contains("approval: ask", bundle, StringComparison.Ordinal);
        Assert.Contains("== model ==", bundle, StringComparison.Ordinal);
        Assert.Contains("id: gpt-4o-mini", bundle, StringComparison.Ordinal);
        Assert.Contains("provider: openai", bundle, StringComparison.Ordinal);
        Assert.Contains("== extensions ==", bundle, StringComparison.Ordinal);
        Assert.Contains("== recent events ==", bundle, StringComparison.Ordinal);
        Assert.Contains("RunStarted", bundle, StringComparison.Ordinal);
        Assert.Contains("TextMessageContent", bundle, StringComparison.Ordinal);
        Assert.Contains("RunFinished", bundle, StringComparison.Ordinal);
        Assert.Contains("t+ms", bundle, StringComparison.Ordinal);
        // The turn's answer is not copied: the bundle measures events, it does not mirror them.
        Assert.DoesNotContain("hi there", bundle, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Debug_never_writes_a_credential_from_the_config()
    {
        const string Key = "sk-test-debug-abcdef-012345";
        using var host = new InteractiveSessionHost(clock: Clock);
        File.WriteAllText(
            host.Temp.File("auth.json"),
            "{\"schemaVersion\":1,\"keys\":{\"openai\":\"" + Key + "\"}}"
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi there");

        // The title is user text the bundle echoes, so this proves the redaction runs over the
        // rendered document: the planted value would otherwise be written verbatim.
        await host.SendLineAsync("/name report " + Key);
        await host.WaitForFrameAsync("session name: report " + Key);

        await host.SendLineAsync("/debug");
        await host.WaitForScrollbackAsync("diagnostic bundle written to ");

        string directory = Path.Combine(host.Temp.File("home"), ".lunate", "diagnostics");
        string bundle = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.txt")));
        Assert.DoesNotContain(Key, bundle, StringComparison.Ordinal);
        Assert.Contains("[redacted:openai]", bundle, StringComparison.Ordinal);
        Assert.Contains("auth keys: openai", bundle, StringComparison.Ordinal);
        // The auth file's contents are never copied into the bundle.
        Assert.DoesNotContain("\"keys\"", bundle, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Debug_with_a_path_writes_there_and_refuses_to_overwrite_it()
    {
        using var host = new InteractiveSessionHost(clock: Clock);
        host.Console.Size = new ConsoleSize(200, 24);
        host.Scrollback.Profile.Width = 400;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi there");
        string destination = host.Temp.File("bundle.txt");

        await host.SendLineAsync("/debug " + destination);
        await host.WaitForScrollbackAsync($"diagnostic bundle written to {destination}");
        Assert.True(File.Exists(destination));
        string first = File.ReadAllText(destination);

        await host.SendLineAsync("/debug " + destination);
        await host.WaitForFrameAsync("refusing to overwrite ");
        Assert.Contains(destination, host.Frames, StringComparison.Ordinal);
        Assert.Equal(first, File.ReadAllText(destination));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Debug_with_more_than_one_token_reports_the_usage()
    {
        using var host = new InteractiveSessionHost(clock: Clock);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.SendLineAsync("/debug one two");

        await host.WaitForFrameAsync("usage: /debug [path]");
        Assert.False(
            Directory.Exists(Path.Combine(host.Temp.File("home"), ".lunate", "diagnostics"))
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public void Write_to_an_existing_destination_never_overwrites_it()
    {
        using var temp = new TempDirectory();
        string destination = temp.File("bundle.txt");
        byte[] existing = "existing content\n"u8.ToArray();
        File.WriteAllBytes(destination, existing);
        DiagnosticBundleInput input = Input(temp) with { Destination = destination };

        Assert.ThrowsAny<IOException>(() =>
            DiagnosticBundle.Write(input, CredentialRedactor.Empty)
        );

        Assert.Equal(existing, File.ReadAllBytes(destination));
    }

    [Fact]
    public void Write_never_writes_the_model_endpoint_userinfo_or_query()
    {
        using var temp = new TempDirectory();
        string destination = temp.File("endpoint-bundle.txt");
        DiagnosticBundleInput anonymous = Input(temp);
        DiagnosticBundleInput input = anonymous with
        {
            Destination = destination,
            Model = anonymous.Model with
            {
                Endpoint = new Uri("https://user:password@host:8080/v1?api_key=abc"),
            },
        };

        DiagnosticBundle.Write(input, CredentialRedactor.Empty);

        string bundle = File.ReadAllText(destination);
        Assert.Contains("https://host:8080/v1", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("user:password", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key=abc", bundle, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_renders_a_run_error_message_as_a_length_not_text()
    {
        const string Sentinel = "SECRET-SENTINEL-RUN-ERROR";
        string message = "upstream exploded: " + Sentinel + " with a long tail of detail";
        var error = new RunError("run_1", message);

        string line = DiagnosticBundle.Describe(error);

        Assert.Contains(
            "message chars=" + message.Length.ToString(CultureInfo.InvariantCulture),
            line,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(Sentinel, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_renders_a_retrying_reason_as_a_length_not_text()
    {
        const string Sentinel = "SECRET-SENTINEL-RETRY-REASON";
        string reason = "rate limited: " + Sentinel + " retry after backoff";
        var retrying = new Retrying("run_1", 2, reason);

        string line = DiagnosticBundle.Describe(retrying);

        Assert.Contains(
            "attempt=2 reason chars=" + reason.Length.ToString(CultureInfo.InvariantCulture),
            line,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(Sentinel, line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bundle_lists_single_file_digests_and_the_dependency_graph()
    {
        using var temp = new TempDirectory();
        var lockFile = new ExtensionLock
        {
            ExtensionId = "acme-tools",
            ExtensionVersion = "1.0.0",
            SourceDigest = "sha256:source",
            PackagesWritten = ["Acme.Core"],
            Sources = ["nuget.org"],
            Toolset = new ExtensionLockToolset
            {
                Tfm = "net10.0",
                Rid = "osx-arm64",
                Runtime = "10.0.0",
                ReferencePack = new ExtensionLockReferencePack
                {
                    Id = "Microsoft.NETCore.App.Ref",
                    Version = "10.0.0",
                    Rule = ReferencePackRules.Exact,
                    Sha512 = "pack-hash",
                },
            },
            Packages =
            [
                new ExtensionLockPackage
                {
                    Id = "Acme.Core",
                    Version = "1.2.3",
                    Sha512 = "core-hash",
                    Requested = "1.2.3",
                    Direct = true,
                    Dependencies = [new ExtensionLockDependency("Acme.Util", "1.0.0")],
                },
                new ExtensionLockPackage
                {
                    Id = "Acme.Util",
                    Version = "1.0.0",
                    Sha512 = "util-hash",
                    Requested = "1.0.0",
                },
            ],
        };
        var candidate = new SingleFileExtension
        {
            Path = temp.File("acme.cs"),
            Scope = ExtensionScope.Project,
            Header = new ExtensionHeader
            {
                Id = "acme-tools",
                Version = "1.0.0",
                Tools = ["fetch"],
            },
            State = SingleFileExtensionState.Built,
            Digest = "sha256:trust",
            SourceDigest = "sha256:source",
            LockHash = "sha256:lock",
            Lock = lockFile,
            BuildKey = "build-key",
            AssemblyPath = temp.File("acme.dll"),
        };

        string bundle = DiagnosticBundle.Render(
            Input(temp, singleFile: [candidate], extensions: "extensions: acme-tools built")
        );

        Assert.Contains("single-file candidates:", bundle, StringComparison.Ordinal);
        Assert.Contains("digest: sha256:trust", bundle, StringComparison.Ordinal);
        Assert.Contains("source digest: sha256:source", bundle, StringComparison.Ordinal);
        Assert.Contains("lock hash: sha256:lock", bundle, StringComparison.Ordinal);
        Assert.Contains("Acme.Core 1.2.3 direct", bundle, StringComparison.Ordinal);
        Assert.Contains("Acme.Util 1.0.0\n", bundle, StringComparison.Ordinal);
        Assert.Contains("Acme.Core -> Acme.Util [1.0.0]", bundle, StringComparison.Ordinal);
    }

    private static DiagnosticBundleInput Input(
        TempDirectory temp,
        IReadOnlyList<SingleFileExtension>? singleFile = null,
        IReadOnlyList<DiagnosticEvent>? events = null,
        string extensions = "extensions: none configured"
    ) =>
        new()
        {
            Destination = temp.File("bundle.txt"),
            GeneratedAt = Clock(),
            SessionId = "ses_1",
            StateRoot = temp.File("state"),
            HomeConfigured = false,
            Settings = new AgentSettings(),
            AuthKeys = [],
            Model = new ModelInfo(
                "gpt-4o-mini",
                "openai",
                Endpoint: new Uri("https://api.openai.com/v1"),
                ContextWindow: 128000,
                SupportsTools: true
            ),
            ModelSource = "catalog",
            Thinking = ThinkingLevel.Off,
            ExtensionsText = extensions,
            SingleFileExtensions = singleFile ?? [],
            Events = events ?? [],
            RecordedEvents = events?.Count ?? 0,
        };

    private static async Task TurnAsync(InteractiveSessionHost host, string input, string answer)
    {
        int before = host.Session.Transcript.Count;
        host.Client.Enqueue(Scripts.Text(answer), Scripts.Stop());
        host.Console.SendText(input);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
            !host.Session.IsRunning && host.Session.Transcript.Count >= before + 2
        );
    }
}
