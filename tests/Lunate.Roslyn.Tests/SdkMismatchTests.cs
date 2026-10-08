using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class SdkMismatchTests
{
    private const string Sample =
        "Microsoft.CodeAnalysis.MSBuild.RemoteInvocationException: An exception of type "
        + "System.InvalidOperationException was thrown: Error while calling hostfxr function "
        + "hostfxr_resolve_sdk2. A compatible .NET SDK was not found.\n"
        + "Requested SDK version: 10.0.401\n"
        + "global.json file: /work/polly/global.json\n";

    [Fact]
    public void A_pinned_missing_sdk_is_explained_with_version_and_global_json()
    {
        var explanation = SdkMismatch.Explain(Sample);

        Assert.NotNull(explanation);
        Assert.Contains("10.0.401", explanation, StringComparison.Ordinal);
        Assert.Contains("/work/polly/global.json", explanation, StringComparison.Ordinal);
        Assert.Contains("install", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_generic_missing_sdk_is_explained_without_a_version()
    {
        var explanation = SdkMismatch.Explain("A compatible .NET SDK was not found.");

        Assert.NotNull(explanation);
        Assert.Contains(".NET SDK", explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrelated_message_is_not_an_sdk_mismatch()
    {
        Assert.Null(SdkMismatch.Explain("The project file could not be evaluated."));
    }
}
