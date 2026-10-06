using Microsoft.CodeAnalysis;

namespace Spike.WorkspaceProbe;

internal sealed record DiagnosticsSummary(int Errors, int Warnings, int Info, int Hidden)
{
    public static DiagnosticsSummary From(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = 0;
        var warnings = 0;
        var info = 0;
        var hidden = 0;
        foreach (var diagnostic in diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Error:
                    errors++;
                    break;
                case DiagnosticSeverity.Warning:
                    warnings++;
                    break;
                case DiagnosticSeverity.Info:
                    info++;
                    break;
                default:
                    hidden++;
                    break;
            }
        }

        return new DiagnosticsSummary(errors, warnings, info, hidden);
    }

    public override string ToString() =>
        $"errors={Errors} warnings={Warnings} info={Info} hidden={Hidden}";
}
