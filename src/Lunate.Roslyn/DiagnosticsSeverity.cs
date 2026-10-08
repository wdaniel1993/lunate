namespace Lunate.Roslyn;

/// <summary>The severities diagnostics results carry; informational output is not reported.</summary>
public enum DiagnosticsSeverity
{
    /// <summary>A compiler error.</summary>
    Error = 0,

    /// <summary>A compiler warning.</summary>
    Warning = 1,
}
