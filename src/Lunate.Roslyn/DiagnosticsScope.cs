namespace Lunate.Roslyn;

/// <summary>Which documents a diagnostics request covers.</summary>
public enum DiagnosticsScope
{
    /// <summary>Files modified since the last successful request (or since load); the default.</summary>
    ChangedFiles = 0,

    /// <summary>Every document in the loaded solution.</summary>
    Solution = 1,
}
