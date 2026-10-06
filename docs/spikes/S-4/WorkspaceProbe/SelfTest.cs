using Microsoft.CodeAnalysis;

namespace Spike.WorkspaceProbe;

internal static class SelfTest
{
    public static int Run()
    {
        var checks = new List<(string Name, Func<(bool Passed, string Detail)> Check)>
        {
            ("median-odd", MedianOdd),
            ("median-even", MedianEven),
            ("median-empty", MedianEmpty),
            ("diagnostics-severities", DiagnosticsSeverities),
            ("inject-error-adds-block", InjectErrorAddsBlock),
            ("remove-error-restores-original", RemoveErrorRestoresOriginal),
            ("toggle-round-trips", ToggleRoundTrips),
        };

        var passed = 0;
        foreach (var (name, check) in checks)
        {
            var (ok, detail) = check();
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: {detail}");
            if (ok)
            {
                passed++;
            }
        }

        Console.WriteLine($"self-test: {passed}/{checks.Count} passed");
        return passed == checks.Count ? 0 : 1;
    }

    private static (bool, string) MedianOdd()
    {
        var median = Stats.Median([3, 1, 2]);
        return (Math.Abs(median - 2) < 0.0001, $"median(3,1,2)={median}");
    }

    private static (bool, string) MedianEven()
    {
        var median = Stats.Median([4, 1, 3, 2]);
        return (Math.Abs(median - 2.5) < 0.0001, $"median(4,1,3,2)={median}");
    }

    private static (bool, string) MedianEmpty()
    {
        var median = Stats.Median([]);
        return (median == 0, $"median()={median}");
    }

    private static (bool, string) DiagnosticsSeverities()
    {
        var diagnostics = new[]
        {
            Create(DiagnosticSeverity.Error),
            Create(DiagnosticSeverity.Warning),
            Create(DiagnosticSeverity.Warning),
            Create(DiagnosticSeverity.Info),
            Create(DiagnosticSeverity.Hidden),
        };
        var summary = DiagnosticsSummary.From(diagnostics);
        var ok = summary == new DiagnosticsSummary(1, 2, 1, 1);
        return (ok, summary.ToString());
    }

    private static Diagnostic Create(DiagnosticSeverity severity)
    {
        var descriptor = new DiagnosticDescriptor(
            "PROBE001",
            "title",
            "message",
            "category",
            severity,
            true
        );
        return Diagnostic.Create(descriptor, Location.None);
    }

    private static (bool, string) InjectErrorAddsBlock()
    {
        const string original = "class A\n{\n}\n";
        var injected = EditScript.InjectError(original);
        var ok =
            EditScript.HasError(injected)
            && injected.StartsWith(original, StringComparison.Ordinal)
            && injected.Contains("not an int", StringComparison.Ordinal);
        return (ok, $"has_error={EditScript.HasError(injected)} length={injected.Length}");
    }

    private static (bool, string) RemoveErrorRestoresOriginal()
    {
        const string original = "class A\n{\n}\n";
        var restored = EditScript.RemoveError(EditScript.InjectError(original));
        return (
            restored == original,
            $"restored_length={restored.Length} original_length={original.Length}"
        );
    }

    private static (bool, string) ToggleRoundTrips()
    {
        const string original = "class A\n{\n}\n";
        var once = EditScript.Toggle(original);
        var twice = EditScript.Toggle(once);
        var ok = EditScript.HasError(once) && !EditScript.HasError(twice) && twice == original;
        return (
            ok,
            $"once_has_error={EditScript.HasError(once)} twice_has_error={EditScript.HasError(twice)}"
        );
    }
}
