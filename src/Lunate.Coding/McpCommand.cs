namespace Lunate.Coding;

/// <summary>
/// <c>lunate mcp &lt;server&gt;</c>: the MCP servers Lunate ships for other hosts. Each subcommand
/// speaks MCP over stdio; stdout carries protocol only.
/// </summary>
internal static class McpCommand
{
    internal static int Run(string[] args, TextWriter errors, CancellationToken ct)
    {
        if (args is ["roslyn"])
        {
            return McpRoslynCommand
                .RunAsync(
                    Environment.CurrentDirectory,
                    Console.OpenStandardInput(),
                    Console.OpenStandardOutput(),
                    errors,
                    ct
                )
                .GetAwaiter()
                .GetResult();
        }

        WriteUsage(errors, args);
        return 2;
    }

    private static void WriteUsage(TextWriter errors, string[] args)
    {
        if (args.Length > 1)
        {
            errors.WriteLine("lunate: mcp takes exactly one server name");
        }
        else if (args is [{ } name])
        {
            errors.WriteLine($"lunate: unknown MCP server '{name}'; available: roslyn");
        }

        errors.WriteLine("Usage: lunate mcp roslyn");
        errors.WriteLine(
            "lunate mcp roslyn serves the read-only C# tools over stdio for another MCP client"
        );
    }
}
