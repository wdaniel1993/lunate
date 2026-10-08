using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class MsBuildBootstrapTests
{
    [Fact]
    public void Initialization_runs_once_and_is_cached()
    {
        var calls = 0;
        MsBuildBootstrap.ResetForTests();
        MsBuildBootstrap.TestRegisterOverride = () =>
        {
            calls++;
            return new BootstrapResult(Succeeded: true, FailureMessage: null);
        };

        try
        {
            var first = MsBuildBootstrap.EnsureInitialized();
            var second = MsBuildBootstrap.EnsureInitialized();

            Assert.True(first.Succeeded);
            Assert.Equal(first, second);
            Assert.Equal(1, calls);
        }
        finally
        {
            MsBuildBootstrap.TestRegisterOverride = null;
            MsBuildBootstrap.ResetForTests();
        }
    }

    [Fact]
    public void A_failed_registration_is_reported_with_its_message()
    {
        MsBuildBootstrap.ResetForTests();
        MsBuildBootstrap.TestRegisterOverride = () => new BootstrapResult(false, "install an SDK");

        try
        {
            var result = MsBuildBootstrap.EnsureInitialized();

            Assert.False(result.Succeeded);
            Assert.Equal("install an SDK", result.FailureMessage);
        }
        finally
        {
            MsBuildBootstrap.TestRegisterOverride = null;
            MsBuildBootstrap.ResetForTests();
        }
    }
}
