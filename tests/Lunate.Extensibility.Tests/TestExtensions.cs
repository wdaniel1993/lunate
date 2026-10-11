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
        string modelProviders = ""
    )
    {
        Directory.CreateDirectory(extensionDirectory);
        string? schema =
            settingsSchema.Length == 0 ? null : $",\"settingsSchema\":{settingsSchema}";
        string? providers =
            modelProviders.Length == 0 ? null : $",\"modelProviders\":{modelProviders}";
        string json =
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"{{apiVersion}}","entryAssembly":"{{entryAssembly}}"{{schema}}{{providers}}}""";
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), json);
    }

    public static string FixtureFile(string fileName) => FixtureFile("HelloExtension", fileName);

    public static string FixtureFile(string fixture, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestExtensions", fixture, fileName);

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
}
