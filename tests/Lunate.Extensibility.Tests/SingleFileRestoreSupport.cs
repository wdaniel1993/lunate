using Lunate.Extensibility;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// A restore engine with no feed behind it: it records how often it was constructed, how often it
/// was asked to restore, and what it was asked. Zero constructions is how a test states "no source
/// repository and no HTTP client existed", which is stronger than "no request happened to be made".
/// </summary>
internal sealed class CountingRestoreEngine : ISingleFileRestoreEngine
{
    private readonly Func<SingleFileRestoreRequest, SingleFileRestoreResult> _respond;

    internal CountingRestoreEngine(Func<SingleFileRestoreRequest, SingleFileRestoreResult> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        _respond = respond;
    }

    internal int Requests { get; private set; }

    internal List<SingleFileRestoreRequest> Seen { get; } = [];

    public Task<SingleFileRestoreResult> RestoreAsync(
        SingleFileRestoreRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests++;
        Seen.Add(request);
        return Task.FromResult(_respond(request));
    }
}

/// <summary>
/// The engine seam as the host uses it: a factory that is created only when a restore runs. The
/// creation count is the feed-request counter the stale tests assert on.
/// </summary>
internal sealed class CountingEngineFactory(CountingRestoreEngine? engine)
{
    internal int Creations { get; private set; }

    internal ISingleFileRestoreEngine? Create()
    {
        Creations++;
        return engine;
    }
}

/// <summary>
/// One package as the fake engine reports it, with the fields a lock needs and nothing else.
/// </summary>
internal static class FakePackage
{
    internal static ExtensionLockPackage Create(
        string id,
        string version,
        bool direct = true,
        string sha512 = "ZmFrZQ==",
        bool generators = false
    ) =>
        new()
        {
            Id = id,
            Version = version,
            Sha512 = sha512,
            Requested = direct ? FormattableString.Invariant($"{id}@{version}") : $"[{version}, )",
            Direct = direct,
            Dependencies = [],
            Files =
            [
                FormattableString.Invariant($"lib/net10.0/{id}.dll"),
                FormattableString.Invariant($"{id}.nuspec"),
            ],
            Compile = [FormattableString.Invariant($"lib/net10.0/{id}.dll")],
            Runtime = [FormattableString.Invariant($"lib/net10.0/{id}.dll")],
            Native = [],
            Analyzers = [],
            Generators = generators,
        };

    internal static ExtensionLockReferencePack ReferencePack(string version = "10.0.5") =>
        new()
        {
            Id = "Microsoft.NETCore.App.Ref",
            Version = version,
            Rule = ReferencePackRules.Exact,
            Assets = ["ref/net10.0/System.Runtime.dll"],
        };
}

/// <summary>
/// A trust prompt that answers only single-file questions, records them, and throws when a packaged
/// question arrives (a restore asking the wrong question is a bug, not something to tolerate).
/// </summary>
internal sealed class RecordingSingleFilePrompt(params bool[] decisions) : IExtensionTrustPrompt
{
    private readonly Queue<bool> _decisions = new(decisions);

    internal int Calls { get; private set; }

    internal List<SingleFileTrustRequest> Requests { get; } = [];

    public ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    ) =>
        throw new InvalidOperationException(
            "a single-file restore must not ask the packaged-extension question"
        );

    public ValueTask<bool> ApproveSingleFileAsync(
        SingleFileTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        Requests.Add(request);
        if (!_decisions.TryDequeue(out bool decision))
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"no scripted answer left for single-file prompt call {Calls}"
                )
            );
        }

        return ValueTask.FromResult(decision);
    }
}
