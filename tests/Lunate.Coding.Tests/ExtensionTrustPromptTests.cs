using System.Globalization;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

public sealed class ExtensionTrustPromptTests
{
    [Fact]
    public async Task Non_interactive_prompt_refuses_and_logs_an_actionable_message()
    {
        var log = new RecordingExtensionLog();
        var prompt = new NonInteractiveTrustPrompt(log);

        bool approved = await prompt.ApproveAsync(
            new ExtensionTrustRequest(
                Descriptor("hello", "/work/.lunate/extensions/hello"),
                FilesChanged: false
            ),
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
        Assert.Contains(
            "project extension 'hello' was not trusted; run an interactive session or 'lunate trust' to approve it.",
            log.Messages
        );
    }

    [Fact]
    public void An_out_of_process_descriptor_states_the_privileges_and_an_in_process_one_does_not()
    {
        string first = TrustFormat.ExtensionQuestion(
            Descriptor("hello", "/work/x", remote: true),
            filesChanged: false
        );
        string reprompt = TrustFormat.ExtensionQuestion(
            Descriptor("hello", "/work/x", remote: true),
            filesChanged: true
        );
        string inProcess = TrustFormat.ExtensionQuestion(
            Descriptor("hello", "/work/x"),
            filesChanged: false
        );

        Assert.Contains(" — it runs with your user privileges", first, StringComparison.Ordinal);
        Assert.Contains(" — it runs with your user privileges", reprompt, StringComparison.Ordinal);
        Assert.DoesNotContain("user privileges", inProcess, StringComparison.Ordinal);
    }

    [Fact]
    public void The_packaged_extension_question_leaves_the_question_mark_to_the_prompt()
    {
        string question = TrustFormat.ExtensionQuestion(
            Descriptor("hello", "/work/.work/x"),
            filesChanged: false
        );
        string reprompt = TrustFormat.ExtensionQuestion(
            Descriptor("hello", "/work/.work/x"),
            filesChanged: true
        );

        // The prompt surface appends the marker for prompts phrased without one; a title that also
        // ends with one renders "??" (the single-file question did exactly that).
        Assert.False(question.EndsWith('?'));
        Assert.False(reprompt.EndsWith('?'));
    }

    [Fact]
    public async Task The_non_interactive_prompt_refuses_a_single_file_and_names_it()
    {
        var log = new RecordingExtensionLog();
        var prompt = new NonInteractiveTrustPrompt(log);

        bool approved = await prompt.ApproveSingleFileAsync(
            new SingleFileTrustRequest
            {
                Path = "/work/.lunate/extensions/acme.cs",
                Id = "acme-tools",
                Version = "1.0.0",
                Packages = ["Octokit@14.0.0"],
            },
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
        Assert.Contains(
            "single-file extension 'acme-tools' at '/work/.lunate/extensions/acme.cs' was not trusted; run an interactive session to approve it.",
            log.Messages
        );
    }

    [Fact]
    public async Task Non_interactive_prompt_does_not_call_a_user_scope_install_a_project_extension()
    {
        var log = new RecordingExtensionLog();
        var prompt = new NonInteractiveTrustPrompt(log);

        await prompt.ApproveAsync(
            new ExtensionTrustRequest(
                Descriptor("hello", "/root/.lunate/extensions/hello") with
                {
                    Scope = ExtensionScope.Global,
                },
                FilesChanged: false
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            "extension 'hello' installed at the user scope was not trusted; run an interactive session or 'lunate trust' to approve it.",
            log.Messages
        );
    }

    internal static ExtensionDescriptor Descriptor(string id, string directory, bool remote = false)
    {
        string runtime = remote
            ? "\"runtime\":{\"command\":\"node\"}"
            : "\"entryAssembly\":\"E.dll\"";
        return new(
            id,
            "0.1.0",
            ExtensionScope.Project,
            directory,
            ExtensionManifest.Parse(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.0.0",{{runtime}}}"""
                ),
                Path.Combine(directory, "extension.json")
            )
        );
    }
}
