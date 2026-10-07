namespace Lunate.Coding;

/// <summary>UI-only details of a <c>write</c> call: display path, created/replaced, line count and diff.</summary>
public sealed record WriteDetails(string Path, bool Created, int Lines, string Diff);
