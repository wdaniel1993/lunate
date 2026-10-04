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
}
