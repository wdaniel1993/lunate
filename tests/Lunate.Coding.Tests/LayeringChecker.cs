namespace Lunate.Coding.Tests;

internal static class LayeringChecker
{
    public static IReadOnlySet<(string From, string To)> AllowedReferences { get; } =
        new HashSet<(string From, string To)>
        {
            ("Lunate.Agent", "Lunate.Ai"),
            ("Lunate.Extensibility", "Lunate.Agent"),
            ("Lunate.Extensibility", "Lunate.Ai"),
            ("Lunate.Extensibility", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility", "Lunate.Extensibility.Remote"),
            // The restore engine is the only project that references the NuGet client libraries;
            // it implements the seam Extensibility declares, so it depends downward on it.
            ("Lunate.Extensibility.Restore", "Lunate.Extensibility"),
            // The compile engine is the only project that references Roslyn for extensions; it
            // implements Extensibility's compile seam, so it depends downward on it too. Both engines
            // are factories in the composition, so neither is loaded until a restore or a compile runs.
            ("Lunate.Extensibility.Compile", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Compile.Tests", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Compile.Tests", "Lunate.Extensibility.Compile"),
            // The MSBL001 both-orders proof builds a real extension through the production restore
            // engine and loads a real workspace through the production Roslyn loader, so it holds
            // the one place where the two package graphs meet.
            ("Lunate.Extensibility.Compile.Tests", "Lunate.Extensibility.Restore"),
            ("Lunate.Extensibility.Compile.Tests", "Lunate.Roslyn"),
            ("Lunate.Extensibility.Restore.Tests", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Restore.Tests", "Lunate.Extensibility.Restore"),
            ("Lunate.Extensibility.Remote", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.Extensibility.Remote"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.Extensibility.Testing"),
            ("Lunate.Agent", "Lunate.Contracts"),
            ("Lunate.Ai", "Lunate.Contracts"),
            // The agent-only ask-user seam rides the contract's internal state object, so the
            // protocol adapters read it as friends (InternalsVisibleTo), not by layering.
            ("Lunate.Extensibility.Abstractions", "Lunate.Contracts"),
            ("Lunate.Extensibility.Testing", "Lunate.Agent"),
            ("Lunate.Extensibility.Testing", "Lunate.Ai"),
            // The kit composes MCP registrations through the production mapping in Coding.
            ("Lunate.Extensibility.Testing", "Lunate.Coding"),
            ("Lunate.Extensibility.Testing", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Testing", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Testing", "Lunate.Extensibility.Remote"),
            ("Lunate.Protocols", "Lunate.Agent"),
            ("Lunate.Roslyn", "Lunate.Extensibility.Abstractions"),
            // The typed host adapter is the only boundary between Coding and the Roslyn feature.
            ("Lunate.Roslyn.BuiltIn", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Roslyn.BuiltIn", "Lunate.Roslyn"),
            ("Lunate.Roslyn.Tests", "Lunate.Roslyn"),
            ("Lunate.Roslyn.Tests", "Lunate.Agent"),
            ("Lunate.Roslyn.Tests", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Coding", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Extensibility"),
            // The composition owns the restore engine factory: it constructs the engine only when
            // a restore runs, so composing a session loads no NuGet assembly.
            ("Lunate.Coding", "Lunate.Extensibility.Restore"),
            // The composition owns the compile factory as well: the first build loads the compiler.
            ("Lunate.Coding", "Lunate.Extensibility.Compile"),
            ("Lunate.Coding", "Lunate.Protocols"),
            // Coding uses the adapter only when constructing its host-supplied built-in factory list.
            ("Lunate.Coding", "Lunate.Roslyn.BuiltIn"),
            ("Lunate.Coding", "Lunate.Tui"),
            ("Lunate.Agent.Tests", "Lunate.Agent"),
            // The prefix-stability test composes the real Coding system prompt to pin the
            // cacheable prefix; the loop under test is still Agent.
            ("Lunate.Agent.Tests", "Lunate.Coding"),
            ("Lunate.Ai.Tests", "Lunate.Ai"),
            ("Lunate.Coding.Tests", "Lunate.Coding"),
            ("Lunate.Coding.Tests", "Lunate.Roslyn.BuiltIn"),
            ("Lunate.Coding.Tests", "HelloExtension"),
            ("Lunate.Coding.Tests", "CommandExtension"),
            ("Lunate.Coding.Tests", "McpExtension"),
            ("Lunate.Coding.Tests", "ModelExtension"),
            ("Lunate.Coding.Tests", "ServiceConsumerExtension"),
            ("Lunate.Coding.Tests", "SkillSourceExtension"),
            ("Lunate.Coding.Tests", "ToolExtension"),
            ("Lunate.Coding.Tests", "TestMcpServer"),
            ("Lunate.Coding.Tests", "UiShowcaseExtension"),
            // The shared TestMcpServer path/marker/process helpers are a BCL-only support project.
            ("Lunate.Coding.Tests", "Lunate.TestSupport"),
            // The shared repository-root locator is a BCL-only support project, so every suite
            // that reads committed fixtures references it instead of carrying its own walk.
            ("Lunate.Agent.Tests", "Lunate.TestSupport"),
            ("Lunate.Ai.Tests", "Lunate.TestSupport"),
            ("Lunate.Extensibility.Compile.Tests", "Lunate.TestSupport"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.TestSupport"),
            ("Lunate.Extensibility.Restore.Tests", "Lunate.TestSupport"),
            ("Lunate.Extensibility.Tests", "Lunate.TestSupport"),
            ("Lunate.Roslyn.Tests", "Lunate.TestSupport"),
            ("Lunate.Tui.Tests", "Lunate.TestSupport"),
            ("Lunate.Protocols.Tests", "Lunate.Protocols"),
            ("Lunate.Protocols.Tests", "Lunate.Coding"),
            ("Lunate.Protocols.Tests", "TestMcpServer"),
            ("Lunate.Protocols.Tests", "Lunate.TestSupport"),
            ("Lunate.Tui.Tests", "Lunate.Tui"),
            ("Lunate.Extensibility.Tests", "Lunate.Agent"),
            ("Lunate.Extensibility.Tests", "Lunate.Coding"),
            ("Lunate.Extensibility.Tests", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Tests", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Tests", "FakeLsp"),
            ("Lunate.Extensibility.Tests", "HelloExtension"),
            ("Lunate.Extensibility.Tests", "MarkerExtension"),
            ("Lunate.Extensibility.Tests", "CommandExtension"),
            ("Lunate.Extensibility.Tests", "McpExtension"),
            ("Lunate.Extensibility.Tests", "ModelExtension"),
            ("Lunate.Extensibility.Tests", "ToolExtension"),
            ("Lunate.Extensibility.Tests", "LeakExtension"),
            ("Lunate.Extensibility.Testing.Tests", "Lunate.Coding"),
            ("Lunate.Extensibility.Testing.Tests", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Testing.Tests", "Lunate.Extensibility.Testing"),
            ("Lunate.Extensibility.Testing.Tests", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Testing.Tests", "HelloExtension"),
            ("Lunate.Extensibility.Testing.Tests", "McpExtension"),
            ("Lunate.Extensibility.Testing.Tests", "ToolExtension"),
            ("Lunate.Extensibility.Testing.Tests", "TestMcpServer"),
            ("Lunate.Extensibility.Testing.Tests", "Lunate.TestSupport"),
            ("HelloExtension", "HelloExtension.Support"),
            ("HelloExtension", "Lunate.Extensibility.Abstractions"),
            ("CommandExtension", "Lunate.Extensibility.Abstractions"),
            ("HelloExtension.Support", "Lunate.Extensibility.Abstractions"),
            ("McpExtension", "Lunate.Extensibility.Abstractions"),
            ("ServiceConsumerExtension", "Lunate.Extensibility.Abstractions"),
            ("SkillSourceExtension", "Lunate.Extensibility.Abstractions"),
            ("ToolExtension", "Lunate.Extensibility.Abstractions"),
            ("ModelExtension", "Lunate.Extensibility.Abstractions"),
            ("ModelRouterExtension", "Lunate.Extensibility.Abstractions"),
            ("ModelRouterExtension.Tests", "Lunate.Ai"),
            ("ModelRouterExtension.Tests", "ModelRouterExtension"),
            ("ModelRouterExtension.Tests", "Lunate.Extensibility.Testing"),
            ("ModelRouterExtension.Tests", "SubagentsExtension"),
            ("FakeLsp", "Lunate.Extensibility.Abstractions"),
            ("TemplateExtension", "Lunate.Extensibility.Abstractions"),
            ("TemplateExtension.Tests", "Lunate.Ai"),
            ("TemplateExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("TemplateExtension.Tests", "Lunate.Extensibility.Testing"),
            ("PermissionGateExtension", "Lunate.Extensibility.Abstractions"),
            ("PermissionGateExtension.Tests", "Lunate.Ai"),
            ("PermissionGateExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("PermissionGateExtension.Tests", "Lunate.Extensibility.Testing"),
            ("MemoryProviderExtension", "Lunate.Extensibility.Abstractions"),
            ("MemoryProviderExtension.Tests", "Lunate.Ai"),
            ("MemoryProviderExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("MemoryProviderExtension.Tests", "Lunate.Extensibility.Testing"),
            ("LspDiagnosticsExtension", "Lunate.Extensibility.Abstractions"),
            // The sample's tests drive the real write/edit tools and unit-test the extension's
            // pure helpers, so they reference the Coding layer and the extension project.
            ("LspDiagnosticsExtension.Tests", "FakeLspServer"),
            ("LspDiagnosticsExtension.Tests", "LspDiagnosticsExtension"),
            ("LspDiagnosticsExtension.Tests", "Lunate.Coding"),
            ("LspDiagnosticsExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("LspDiagnosticsExtension.Tests", "Lunate.Extensibility.Testing"),
            // The MCP sample's tests use only the kit and the extension (the public surface).
            ("McpFromExtensionExtension", "Lunate.Extensibility.Abstractions"),
            ("McpFromExtensionExtension.Tests", "FakeMcpServer"),
            ("McpFromExtensionExtension.Tests", "McpFromExtensionExtension"),
            ("McpFromExtensionExtension.Tests", "Lunate.Extensibility.Testing"),
            // The subagents sample's tests use only the kit and the extension (the public surface).
            ("SubagentsExtension", "Lunate.Extensibility.Abstractions"),
            ("SubagentsExtension.Tests", "SubagentsExtension"),
            ("SubagentsExtension.Tests", "Lunate.Extensibility.Testing"),
            // The worktree-tasks sample is Abstractions-only; its tests build the child's real
            // coding tools, so they reference the Coding layer and the extension project.
            ("WorktreeTasksExtension", "Lunate.Extensibility.Abstractions"),
            ("WorktreeTasksExtension.Tests", "WorktreeTasksExtension"),
            ("WorktreeTasksExtension.Tests", "Lunate.Coding"),
            ("WorktreeTasksExtension.Tests", "Lunate.Extensibility.Testing"),
            // The code-mode extension itself references Abstractions only; its tests bridge the
            // real read tool, so they also reference the Coding layer and the extension project.
            ("CodeModeExtension", "Lunate.Extensibility.Abstractions"),
            ("CodeModeExtension.Tests", "CodeModeExtension"),
            ("CodeModeExtension.Tests", "Lunate.Coding"),
            ("CodeModeExtension.Tests", "Lunate.Extensibility.Testing"),
            // The node-remote sample is a real Node child; its tests drive the conformance
            // runner and the real host.
            ("NodeRemoteExtension.Tests", "Lunate.Extensibility"),
            ("NodeRemoteExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("NodeRemoteExtension.Tests", "Lunate.Extensibility.Testing"),
            // The ui-showcase sample is Abstractions-only; its tests use the kit and reference
            // the extension project itself (the sibling pattern).
            ("UiShowcaseExtension", "Lunate.Extensibility.Abstractions"),
            ("UiShowcaseExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("UiShowcaseExtension.Tests", "Lunate.Extensibility.Testing"),
            ("UiShowcaseExtension.Tests", "UiShowcaseExtension"),
        };

    /// <summary>
    /// The assemblies the host loads once and every extension binds to: the contract, and the host
    /// types its signatures expose (<c>ToolContext</c>, <c>ToolResult</c>, the provider surface).
    /// An extension may reference them — compile-only, never copied — and nothing else of the host.
    /// </summary>
    public static IReadOnlySet<string> ExtensionHostAssemblies { get; } =
        new HashSet<string> { "Lunate.Extensibility.Abstractions", "Lunate.Contracts" };

    /// <summary>
    /// An extension is a project outside the host's own <c>Lunate.*</c> set — a sample, the template,
    /// a test extension. Its own test project is not: it runs the extension in-process, so it needs
    /// the host's assemblies on disk.
    /// </summary>
    public static bool IsExtensionProject(string name) =>
        !name.StartsWith("Lunate.", StringComparison.Ordinal)
        && !name.EndsWith(".Tests", StringComparison.Ordinal);

    /// <summary>Whether an edge is an extension reaching for one of the host's shared assemblies.</summary>
    public static bool IsAllowedExtensionReference((string From, string To) reference) =>
        IsExtensionProject(reference.From) && ExtensionHostAssemblies.Contains(reference.To);

    public static IReadOnlySet<string> AllowedAgentRuntimePackages { get; } =
        new HashSet<string> { "Microsoft.Extensions.AI.Abstractions" };

    public static IReadOnlyList<string> FindViolations(
        IEnumerable<(string From, string To)> references
    )
    {
        var violations = new List<string>();

        foreach (var reference in references)
        {
            if (!AllowedReferences.Contains(reference) && !IsAllowedExtensionReference(reference))
            {
                violations.Add($"{reference.From} -> {reference.To} is not an allowed reference");
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> FindPackageViolations(
        IEnumerable<(string Package, bool IsBuildOnly)> packages
    )
    {
        var violations = new List<string>();

        foreach (var package in packages)
        {
            if (package.IsBuildOnly || AllowedAgentRuntimePackages.Contains(package.Package))
            {
                continue;
            }

            violations.Add($"{package.Package} is not an allowed runtime package for Lunate.Agent");
        }

        return violations;
    }
}
