namespace Lunate.Coding;

/// <summary>UI-only details of an <c>edit</c> call: display path, replacement line range, match tier and diff.</summary>
public sealed record EditDetails(
    string Path,
    int FirstLine,
    int LastLine,
    string MatchTier,
    string Diff
);
