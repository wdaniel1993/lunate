namespace Lunate.Coding;

/// <summary>The <c>--help</c> text: one process synopsis per mode, then the launch flags.</summary>
internal static class CliHelp
{
    internal static void Write(TextWriter output)
    {
        output.WriteLine("Lunate - a coding agent for the terminal.");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  lunate -p [--json] [--yolo] <prompt>");
        output.WriteLine(
            "                                 run one prompt and print the final answer to stdout;"
        );
        output.WriteLine(
            "                                 --json streams every event as one JSON line, --yolo"
        );
        output.WriteLine("                                 approves every tool call for this run");
        output.WriteLine(
            "  lunate                         start the interactive session on a terminal"
        );
        output.WriteLine(
            "                                 commands: /model /new /resume /name /session /compact /mcp /extensions /trust /quit"
        );
        output.WriteLine(
            "  lunate --no-extensions         start any mode without loading extensions (their"
        );
        output.WriteLine(
            "                                 hooks, widgets and MCP servers are absent; configured"
        );
        output.WriteLine("                                 mcp.json servers still run)");
        output.WriteLine(
            "  lunate --acp                   serve the Agent Client Protocol over stdio for"
        );
        output.WriteLine(
            "                                 editors (nothing but protocol on stdout)"
        );
        output.WriteLine("  lunate --version               print the version");
        output.WriteLine("  lunate --help                  show this help");
        output.WriteLine("  lunate --discover <name-or-url>");
        output.WriteLine(
            "                                 list an OpenAI-compatible endpoint's models and"
        );
        output.WriteLine(
            "                                 print a models.json draft to stdout (nothing is written)"
        );
        output.WriteLine("  lunate extensions [--json]");
        output.WriteLine(
            "                                 list discovered extensions with scope, state,"
        );
        output.WriteLine(
            "                                 load time and contributions; --json prints a"
        );
        output.WriteLine(
            "                                 machine-readable array (exit 1 when an extension"
        );
        output.WriteLine("                                 fails to load)");
        output.WriteLine("  lunate trust [--yes [--digest <sha>]] [--json] [--clear]");
        output.WriteLine(
            "                                 show this repository's trust records and pending"
        );
        output.WriteLine(
            "                                 project-extension/mcp.json/server decisions (exit 3"
        );
        output.WriteLine(
            "                                 when pending); --yes records exactly what it prints —"
        );
        output.WriteLine(
            "                                 for checkouts you control, never untrusted PRs; gate"
        );
        output.WriteLine(
            "                                 CI with --digest <sha>; --json for scripts; --clear"
        );
        output.WriteLine("                                 removes this repository's records");
        output.WriteLine();
        output.WriteLine("Session and per-run options (interactive and print):");
        output.WriteLine(
            "  -c, --continue                 continue the newest session recorded for this"
        );
        output.WriteLine("                                 working directory");
        output.WriteLine(
            "  -r, --resume                   choose a session from the interactive picker"
        );
        output.WriteLine(
            "                                 (print mode cannot draw it; use --session or"
        );
        output.WriteLine("                                 --continue)");
        output.WriteLine("  --session <id|path>            load one session by id or file path");
        output.WriteLine("  --no-session                   run without writing a session file");
        output.WriteLine(
            "  --model <id>                   use this model for the run (wins over settings)"
        );
        output.WriteLine("  --thinking <level>             off, low, medium or high for the run");
        output.WriteLine(
            "  --tools <list>                 expose only these comma-separated tool names"
        );
        output.WriteLine("  --no-tools                     expose no tools for the run");
        output.WriteLine(
            "  --system-prompt <text>         replace the composed system prompt wholesale,"
        );
        output.WriteLine(
            "                                 including the tool-call conventions: the caller"
        );
        output.WriteLine("                                 owns them");
        output.WriteLine(
            "  --append-system-prompt <text>  append a section after the composed system prompt"
        );
        output.WriteLine(
            "A value may also be given as --flag=value; this is how to pass a value that begins with '-'."
        );
        output.WriteLine();
        output.WriteLine(
            "Selection flags are mutually exclusive with each other and --no-session."
        );
    }
}
