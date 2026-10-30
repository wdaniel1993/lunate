namespace LspDiagnosticsExtension;

/// <summary>
/// One diagnostic a language server published for a document. Positions are LSP units (0-based);
/// the model-facing text converts to 1-based once, at the shaping boundary.
/// </summary>
public sealed record Diagnostic(int Severity, int Line, int Character, string Code, string Message)
{
    /// <summary>LSP severity 1 (error) or 2 (warning); information and hints are not attached.</summary>
    public bool IsErrorOrWarning => Severity is 1 or 2;
}
