namespace Lunate.Coding.Tests;

public sealed class LayeringCheckerTests
{
    [Fact]
    public void FindViolations_reports_upward_reference()
    {
        var violations = LayeringChecker.FindViolations([("Lunate.Ai", "Lunate.Agent")]);

        var violation = Assert.Single(violations);
        Assert.Contains("Lunate.Ai", violation, StringComparison.Ordinal);
        Assert.Contains("Lunate.Agent", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void FindPackageViolations_reports_disallowed_runtime_package()
    {
        var violations = LayeringChecker.FindPackageViolations([
            ("Microsoft.Extensions.Logging.Abstractions", false),
        ]);

        var violation = Assert.Single(violations);
        Assert.Contains(
            "Microsoft.Extensions.Logging.Abstractions",
            violation,
            StringComparison.Ordinal
        );
        Assert.Contains("Lunate.Agent", violation, StringComparison.Ordinal);
    }
}
