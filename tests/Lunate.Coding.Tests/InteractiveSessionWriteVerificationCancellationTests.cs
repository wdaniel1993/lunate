using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionWriteVerificationCancellationTests
{
    [Fact]
    public async Task Escape_cancels_the_active_verification_without_writing()
    {
        using var temp = new TempDirectory();
        var verifier = new WaitingVerifier();
        using var host = new InteractiveSessionHost(
            workingDirectory: temp.Root,
            extensions: new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
                BuiltIns = [VerifierExtension.Create(verifier)],
            }
        );
        const string original = "before\n";
        File.WriteAllText(temp.File("code.cs"), original);
        host.Client.Enqueue(
            Scripts.Call(
                "call-1",
                "write",
                Scripts.Args(("path", "code.cs"), ("content", "after\n"))
            ),
            Scripts.ToolCalls()
        );
        Task run = host.RunAsync();

        host.Console.SendText("update code");
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();
        host.Console.SendText("y");
        await verifier.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        Assert.True(host.Session.IsRunning);

        host.Console.SendEscape();

        await verifier.Canceled.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        Assert.Equal(original, File.ReadAllText(temp.File("code.cs")));

        host.Console.Complete();
        await run;
    }

    private sealed class VerifierExtension(IWriteVerifier verifier) : IExtensionFactory
    {
        public static BuiltInExtension Create(IWriteVerifier verifier)
        {
            const string path = "host/waiter/extension.json";
            return new BuiltInExtension(
                path,
                ExtensionManifest.Parse(
                    """{"id":"waiter","version":"0.1.0","apiVersion":"^1.13.0","builtin":true,"writeVerifiers":[{"name":"waiter","patterns":["*.cs"]}]}""",
                    path
                ),
                () => new VerifierExtension(verifier)
            );
        }

        public IExtension Create(IExtensionContext context)
        {
            context.RegisterWriteVerifier(new WriteVerifierRegistration("waiter", verifier));
            return new EmptyExtension();
        }
    }

    private sealed class WaitingVerifier : IWriteVerifier
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<WriteVerificationResult> VerifyAsync(
            WriteVerificationRequest request,
            CancellationToken cancellationToken
        )
        {
            Started.TrySetResult();
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                Canceled.TrySetResult()
            );
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new WriteVerificationResult(WriteVerificationStatus.Verified);
        }
    }

    private sealed class EmptyExtension : IExtension { }
}
