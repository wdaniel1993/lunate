using Lunate.Agent;

namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// Answers a manifest <c>toolActivation</c> rule against a workspace root, so the host decides a
/// tool's availability once per composition without asking the extension. Filesystem conditions
/// never throw: a root the host cannot read reports false, exactly as the flagship C# workspace
/// gate reads an unreadable root. The git condition follows the same file-system-only identity the
/// rest of the host sees, resolved through <see cref="GitIdentity"/> — or taken as-is where a
/// caller already holds the resolved common dir.
/// </summary>
public static class ToolActivationEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="rule"/> for the workspace root: a <c>null</c> rule (the manifest
    /// declared none) is always active. The git condition re-detects the repository identity from
    /// the root; use the three-argument overload where the caller already resolved it.
    /// </summary>
    public static bool Evaluate(ExtensionToolActivationRule? rule, string worktreeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        (_, string? gitCommonDir) = GitIdentity.Detect(worktreeRoot);
        return Evaluate(rule, worktreeRoot, gitCommonDir);
    }

    /// <summary>
    /// Evaluates <paramref name="rule"/> with the caller's resolved git identity: a non-null
    /// <paramref name="gitCommonDir"/> is a repository and null is not, without re-detection over
    /// the root — the host's notion of the workspace decides, not the evaluator's own walk.
    /// </summary>
    public static bool Evaluate(
        ExtensionToolActivationRule? rule,
        string worktreeRoot,
        string? gitCommonDir
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        if (rule is null)
        {
            return true;
        }

        return rule.When switch
        {
            ExtensionToolActivationRule.Always => true,
            ExtensionToolActivationRule.WorkspaceRootHasFile => RootHasFile(worktreeRoot, rule),
            ExtensionToolActivationRule.WorkspaceIsGitRepository => gitCommonDir is not null,
            // The manifest parser produces the closed condition set; an unknown condition is a
            // host/extension contract drift and never activates a tool by accident.
            _ => false,
        };
    }

    /// <summary>
    /// A parser pattern is a file name whose <c>*</c> characters match any characters within one
    /// name segment — the parser refuses path separators, so a match never crosses one, and a
    /// pattern without <c>*</c> is an exact ordinal name. Direct children only; a root that cannot
    /// be read offers no files.
    /// </summary>
    private static bool RootHasFile(string worktreeRoot, ExtensionToolActivationRule rule)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(worktreeRoot))
            {
                string name = Path.GetFileName(file);
                foreach (string pattern in rule.Patterns)
                {
                    if (Matches(pattern, name))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable root activates nothing, as it would outside any condition.
        }

        return false;
    }

    private static bool Matches(string pattern, string name)
    {
        // Ordinary glob with backtracking: '*' matches any characters within one name segment
        // (the parser refuses separators, so nothing can cross one); every other character
        // compares ordinal, so a pattern without '*' is the exact file name.
        int patternIndex = 0;
        int nameIndex = 0;
        int starPattern = -1;
        int starName = 0;
        while (nameIndex < name.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starPattern = patternIndex++;
                starName = nameIndex;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == name[nameIndex])
            {
                patternIndex++;
                nameIndex++;
                continue;
            }

            if (starPattern >= 0)
            {
                // Let the most recent '*' swallow one more character and try the suffix again.
                patternIndex = starPattern + 1;
                nameIndex = ++starName;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
