namespace Lunate.Coding;

/// <summary>
/// The optional search tools a user can enable beside the core set. One table keeps the settings
/// validator, the environment parser, the launch flag and the factory's registration agreeing on
/// the names and on the accepted-values error.
/// </summary>
internal static class OptionalTools
{
    /// <summary>The names that can be enabled, in their documented order.</summary>
    internal static readonly IReadOnlyList<string> Names = ["grep", "find", "ls"];

    /// <summary>The core tool names an extra entry must never collide with.</summary>
    internal static readonly IReadOnlyList<string> CoreNames = ["read", "write", "edit", "bash"];

    /// <summary>The accepted values as the error messages list them.</summary>
    internal const string Accepted = "'grep', 'find' or 'ls'";

    /// <summary>Whether <paramref name="name"/> is one of the optional tool names.</summary>
    internal static bool IsName(string? name) =>
        name is not null && Names.Contains(name, StringComparer.Ordinal);

    /// <summary>The actionable error for one refused entry.</summary>
    internal static string Error(string subject, string value) =>
        CoreNames.Contains(value, StringComparer.Ordinal)
            ? $"{subject} cannot name the core tool '{value}'; accepts only {Accepted}"
            : $"{subject} accepts only {Accepted}, but was '{value}'";
}
