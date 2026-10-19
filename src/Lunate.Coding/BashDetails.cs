namespace Lunate.Coding;

/// <summary>UI-only details of a <c>bash</c> call: the command's exit code (the footer text stays for the model).</summary>
public sealed record BashDetails(int ExitCode);
