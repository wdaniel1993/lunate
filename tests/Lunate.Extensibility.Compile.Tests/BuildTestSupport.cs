using Lunate.Extensibility;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>An engine with no feed behind it: it reports the fixture's resolution and counts asks.</summary>
internal sealed class FakeRestoreEngine(
    CompileFixture fixture,
    IReadOnlyList<ExtensionLockPackage> packages
) : ISingleFileRestoreEngine
{
    internal int Requests { get; private set; }

    public Task<SingleFileRestoreResult> RestoreAsync(
        SingleFileRestoreRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests++;
        return Task.FromResult(
            new SingleFileRestoreResult
            {
                Packages = packages,
                ReferencePack = fixture.ReferencePack,
                Sources = ["local"],
                GlobalPackagesFolder = fixture.PackagesFolder,
            }
        );
    }
}

/// <summary>The compile factory as the builder uses it: created only when a compile will run.</summary>
internal sealed class CountingCompilerFactory
{
    private readonly ISingleFileCompiler _compiler =
        new Lunate.Extensibility.Compile.RoslynSingleFileCompiler();

    internal int Creations { get; private set; }

    internal ISingleFileCompiler? Create()
    {
        Creations++;
        return _compiler;
    }
}

/// <summary>Refuses the single-file question: what a user who will not trust the edited bytes does.</summary>
internal sealed class DecliningPrompt : IExtensionTrustPrompt
{
    public ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("a single file must not ask the packaged question");

    public ValueTask<bool> ApproveSingleFileAsync(
        SingleFileTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}

/// <summary>Approves every single-file question, so a test can state what an approving user did.</summary>
internal sealed class ApprovingPrompt : IExtensionTrustPrompt
{
    internal int Calls { get; private set; }

    public ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("a single file must not ask the packaged question");

    public ValueTask<bool> ApproveSingleFileAsync(
        SingleFileTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return ValueTask.FromResult(true);
    }
}
