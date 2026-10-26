using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

internal static class TestExtensions
{
    public static ExtensionHostOptions Options(string storePath, IExtensionLog? log = null) =>
        new() { StorePath = storePath, Log = log };

    public static string GlobalExtensionDirectory(TempDirectory temp, string id) =>
        Path.Combine(temp.Subdirectory("store"), "extensions", id);

    public static string ProjectExtensionDirectory(string workingDirectory, string id) =>
        Path.Combine(workingDirectory, ".lunate", "extensions", id);

    public static void WriteManifest(
        string extensionDirectory,
        string id,
        string entryAssembly = "HelloExtension.dll",
        string apiVersion = "^1.0.0",
        string settingsSchema = "",
        string modelProviders = "",
        string tools = "",
        string commands = ""
    )
    {
        Directory.CreateDirectory(extensionDirectory);
        string? schema =
            settingsSchema.Length == 0 ? null : $",\"settingsSchema\":{settingsSchema}";
        string? providers =
            modelProviders.Length == 0 ? null : $",\"modelProviders\":{modelProviders}";
        string? declaredTools = tools.Length == 0 ? null : $",\"tools\":{tools}";
        string? declaredCommands = commands.Length == 0 ? null : $",\"commands\":{commands}";
        string json =
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"{{apiVersion}}","entryAssembly":"{{entryAssembly}}"{{schema}}{{providers}}{{declaredTools}}{{declaredCommands}}}""";
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), json);
    }

    public static string FixtureFile(string fileName) => FixtureFile("HelloExtension", fileName);

    public static string FixtureFile(string fixture, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestExtensions", fixture, fileName);

    /// <summary>
    /// A host assembly from the test's own output directory. An extension's build no longer carries
    /// one (the host owns it), so a fixture that deliberately ships a copy — to prove the load
    /// ignores it — must take the host's.
    /// </summary>
    public static string HostAssembly(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, fileName);

    public static void CopyHostAssembly(string destinationDirectory, params string[] fileNames)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string fileName in fileNames)
        {
            File.Copy(
                HostAssembly(fileName),
                Path.Combine(destinationDirectory, fileName),
                overwrite: true
            );
        }
    }

    public static void CopyFixture(string destinationDirectory, params string[] fileNames)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string fileName in fileNames)
        {
            File.Copy(
                FixtureFile(fileName),
                Path.Combine(destinationDirectory, fileName),
                overwrite: true
            );
        }
    }

    public static string InstallHelloExtension(TempDirectory temp, string id)
    {
        string directory = GlobalExtensionDirectory(temp, id);
        CopyFixture(directory, "HelloExtension.dll", "HelloExtension.Support.dll");
        WriteManifest(directory, id);
        return directory;
    }

    public static string InstallMcpExtension(TempDirectory temp, string id)
    {
        string directory = GlobalExtensionDirectory(temp, id);
        Directory.CreateDirectory(directory);
        File.Copy(
            FixtureFile("McpExtension", "McpExtension.dll"),
            Path.Combine(directory, "McpExtension.dll"),
            overwrite: true
        );
        WriteManifest(directory, id, entryAssembly: "McpExtension.dll");
        return directory;
    }

    public static string InstallProjectHelloExtension(string workingDirectory, string id)
    {
        string directory = ProjectExtensionDirectory(workingDirectory, id);
        CopyFixture(directory, "HelloExtension.dll", "HelloExtension.Support.dll");
        WriteManifest(directory, id);
        return directory;
    }

    /// <summary>
    /// The every-registration fixture: its factory contributes one of each kind, then logs a
    /// cancellation marker, so a test can make its load fail (a declared model id that the code
    /// already serves) or cancel it (the marker fires the test's own token source).
    /// </summary>
    public static string InstallLeakExtension(
        TempDirectory temp,
        string id,
        string tools = """["leak_probe"]""",
        string commands = """["leak-probe"]""",
        string modelProviders = ""
    )
    {
        string directory = GlobalExtensionDirectory(temp, id);
        Directory.CreateDirectory(directory);
        File.Copy(
            FixtureFile("LeakExtension", "LeakExtension.dll"),
            Path.Combine(directory, "LeakExtension.dll"),
            overwrite: true
        );
        WriteManifest(
            directory,
            id,
            entryAssembly: "LeakExtension.dll",
            tools: tools,
            commands: commands,
            modelProviders: modelProviders
        );
        return directory;
    }

    /// <summary>
    /// Rewrites the leak fixture's manifest without touching its assembly: after a failed load the
    /// CLR may still have the DLL mapped, and Windows refuses to overwrite it. A repair that only
    /// changes the manifest is the honest way to make the retry a first load again.
    /// </summary>
    public static string RepairLeakManifest(
        TempDirectory temp,
        string id,
        string tools = """["leak_probe"]""",
        string commands = """["leak-probe"]"""
    )
    {
        string directory = GlobalExtensionDirectory(temp, id);
        WriteManifest(
            directory,
            id,
            entryAssembly: "LeakExtension.dll",
            tools: tools,
            commands: commands
        );
        return directory;
    }

    public static string InstallToolExtension(
        TempDirectory temp,
        string id,
        string tools,
        string? settings = null
    )
    {
        string directory = GlobalExtensionDirectory(temp, id);
        Directory.CreateDirectory(directory);
        File.Copy(
            FixtureFile("ToolExtension", "ToolExtension.dll"),
            Path.Combine(directory, "ToolExtension.dll"),
            overwrite: true
        );
        WriteManifest(directory, id, entryAssembly: "ToolExtension.dll", tools: tools);
        if (settings is not null)
        {
            WriteSettings(temp, id, settings);
        }

        return directory;
    }

    public static string InstallCommandExtension(
        TempDirectory temp,
        string id,
        string commands,
        string? settings = null
    )
    {
        string directory = GlobalExtensionDirectory(temp, id);
        Directory.CreateDirectory(directory);
        File.Copy(
            FixtureFile("CommandExtension", "CommandExtension.dll"),
            Path.Combine(directory, "CommandExtension.dll"),
            overwrite: true
        );
        WriteManifest(directory, id, entryAssembly: "CommandExtension.dll", commands: commands);
        if (settings is not null)
        {
            WriteSettings(temp, id, settings);
        }

        return directory;
    }

    public static void WriteSettings(TempDirectory temp, string id, string settings)
    {
        string directory = Path.Combine(temp.Subdirectory("store"), "extensions-settings");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".json"), settings);
    }
}
