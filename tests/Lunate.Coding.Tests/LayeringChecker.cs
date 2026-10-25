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
            ("Lunate.Extensibility.Restore.Tests", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Restore.Tests", "Lunate.Extensibility.Restore"),
            ("Lunate.Extensibility.Remote", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.Extensibility.Remote"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Remote.Tests", "Lunate.Extensibility.Testing"),
            ("Lunate.Extensibility.Abstractions", "Lunate.Agent"),
            ("Lunate.Extensibility.Testing", "Lunate.Agent"),
            ("Lunate.Extensibility.Testing", "Lunate.Ai"),
            // The kit composes MCP registrations through the production mapping in Coding.
            ("Lunate.Extensibility.Testing", "Lunate.Coding"),
            ("Lunate.Extensibility.Testing", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Testing", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Testing", "Lunate.Extensibility.Remote"),
            ("Lunate.Protocols", "Lunate.Agent"),
            ("Lunate.Roslyn", "Lunate.Agent"),
            ("Lunate.Roslyn.Tests", "Lunate.Roslyn"),
            ("Lunate.Roslyn.Tests", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Extensibility"),
            // The composition owns the restore engine factory: it constructs the engine only when
            // a restore runs, so composing a session loads no NuGet assembly.
            ("Lunate.Coding", "Lunate.Extensibility.Restore"),
            // The composition owns the compile factory as well: the first build loads the compiler.
            ("Lunate.Coding", "Lunate.Extensibility.Compile"),
            ("Lunate.Coding", "Lunate.Protocols"),
            // The composition owns the per-workspace Roslyn services and composes the cs_* tools.
            ("Lunate.Coding", "Lunate.Roslyn"),
            ("Lunate.Coding", "Lunate.Tui"),
            ("Lunate.Agent.Tests", "Lunate.Agent"),
            // The prefix-stability test composes the real Coding system prompt to pin the
            // cacheable prefix; the loop under test is still Agent.
            ("Lunate.Agent.Tests", "Lunate.Coding"),
            ("Lunate.Ai.Tests", "Lunate.Ai"),
            ("Lunate.Coding.Tests", "Lunate.Coding"),
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
            ("Lunate.Extensibility.Tests", "CommandExtension"),
            ("Lunate.Extensibility.Tests", "McpExtension"),
            ("Lunate.Extensibility.Tests", "ModelExtension"),
            ("Lunate.Extensibility.Tests", "ToolExtension"),
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

    public static IReadOnlySet<string> AllowedAgentRuntimePackages { get; } =
        new HashSet<string> { "Microsoft.Extensions.AI.Abstractions" };

    public static IReadOnlyList<string> FindViolations(
        IEnumerable<(string From, string To)> references
    )
    {
        var violations = new List<string>();

        foreach (var reference in references)
        {
            if (!AllowedReferences.Contains(reference))
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
