using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Extensibility;
using NuGet.Frameworks;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The walker against the resolver it imitates: real package graphs, restored by the SDK's own NuGet
/// (<c>dotnet restore</c>) and by the walker, from the same feed and the same cache. Every version
/// must agree for a graph NuGet resolves cleanly; a graph NuGet warns about or fails must be refused
/// by the walker with the same code. The corpus lives behind a skip because the rules only mean
/// something next to the SDK that owns them.
/// </summary>
public sealed class NuGetConformanceTests
{
    private static readonly string[] Codes = ["NU1605", "NU1608", "NU1107"];

    private static readonly Dictionary<string, (string Id, string Version)[]> Corpus = new()
    {
        ["hosting"] =
        [
            ("Microsoft.Extensions.Hosting", "10.0.5"),
            ("Microsoft.Extensions.Logging.Abstractions", "10.0.5"),
        ],
        ["octokit"] = [("Octokit", "14.0.0")],
        // Its dependency carries a bound: Microsoft.Testing.Platform [2.4.0,3.0.0), with 2.4.1
        // published above the minimum the graph resolves to.
        ["upper-bound"] = [("Microsoft.Testing.Extensions.TrxReport.Abstractions", "2.4.0")],
        // xunit declares xunit.core [2.9.3]: an exact range, so both bounds of an edge are in play.
        ["exact-range"] = [("xunit", "2.9.3")],
        ["floating"] = [("Newtonsoft.Json", "13.*")],
        // A directly declared version below what the hosting graph requires: NuGet's NU1605.
        ["downgrade"] =
        [
            ("Microsoft.Extensions.Hosting", "10.0.5"),
            ("Microsoft.Extensions.Logging.Abstractions", "8.0.1"),
        ],
    };

    [Theory]
    [InlineData("hosting")]
    [InlineData("octokit")]
    [InlineData("upper-bound")]
    [InlineData("exact-range")]
    [InlineData("floating")]
    [InlineData("downgrade")]
    public async Task The_walker_agrees_with_dotnet_restore(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (string Id, string Version)[] declared = Corpus[name];
        if (!await SdkAvailableAsync(cancellationToken))
        {
            Assert.Skip("the .NET SDK is not installed; the conformance corpus runs where it is.");
            return;
        }

        using var temp = new TempDirectory();
        await WriteProjectAsync(temp.Root, declared, cancellationToken);
        Restore restore = await RestoreAsync(temp.Root, cancellationToken);

        string? refusal = Codes.FirstOrDefault(code =>
            restore.Output.Contains(code, StringComparison.Ordinal)
        );
        if (refusal is not null)
        {
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    ResolveAsync(temp.Root, declared, cancellationToken)
            );
            Assert.Contains(refusal, failure.Message, StringComparison.Ordinal);
            return;
        }

        Assert.True(
            restore.ExitCode == 0,
            $"dotnet restore failed without one of {string.Join(", ", Codes)}:\n{restore.Output}"
        );

        Dictionary<string, string> expected = ReadAssets(temp.Root);
        Assert.NotEmpty(expected);
        foreach ((string id, _) in declared)
        {
            Assert.True(
                expected.ContainsKey(id),
                $"{name}: dotnet restore did not restore '{id}'."
            );
        }

        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved = await ResolveAsync(
            temp.Root,
            declared,
            cancellationToken
        );
        Dictionary<string, string> actual = resolved.ToDictionary(
            package => package.Id,
            package => package.Version.ToNormalizedString(),
            StringComparer.OrdinalIgnoreCase
        );

        foreach ((string id, string version) in expected)
        {
            Assert.True(
                actual.TryGetValue(id, out string? chosen),
                FormattableString.Invariant($"{name}: the walker is missing '{id}' {version}.")
            );
            Assert.Equal(version, chosen);
        }

        foreach (string id in actual.Keys)
        {
            Assert.True(
                expected.ContainsKey(id),
                FormattableString.Invariant($"{name}: the walker resolved '{id}', NuGet did not.")
            );
        }
    }

    private static async Task<IReadOnlyList<NuGetDependencyWalker.Resolved>> ResolveAsync(
        string root,
        IReadOnlyList<(string Id, string Version)> declared,
        CancellationToken cancellationToken
    )
    {
        NuGetPackageFeed feed = NuGetPackageFeed.Create(root, [], observer: null);
        return await new NuGetDependencyWalker(feed, NuGetFramework.Parse("net10.0")).ResolveAsync(
            [
                .. declared.Select(package => new ExtensionPackageReference(
                    package.Id,
                    package.Version,
                    FormattableString.Invariant($"{package.Id}@{package.Version}")
                )),
            ],
            cancellationToken
        );
    }

    /// <summary>The packages NuGet restored, from the assets file restore wrote: id to version.</summary>
    private static Dictionary<string, string> ReadAssets(string root)
    {
        using JsonDocument assets = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "obj", "project.assets.json"))
        );
        Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase);
        foreach (
            JsonProperty library in assets.RootElement.GetProperty("libraries").EnumerateObject()
        )
        {
            if (
                !library.Value.TryGetProperty("type", out JsonElement type)
                || type.GetString() != "package"
            )
            {
                continue;
            }

            string[] parts = library.Name.Split('/');
            versions[parts[0]] = parts[1];
        }

        return versions;
    }

    private static async Task WriteProjectAsync(
        string root,
        IReadOnlyList<(string Id, string Version)> declared,
        CancellationToken cancellationToken
    )
    {
        string references = string.Join(
            "\n",
            declared.Select(package =>
                FormattableString.Invariant(
                    $"    <PackageReference Include=\"{package.Id}\" Version=\"{package.Version}\" />"
                )
            )
        );
        string project =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            + "  <PropertyGroup>\n"
            + "    <TargetFramework>net10.0</TargetFramework>\n"
            + "  </PropertyGroup>\n"
            + "  <ItemGroup>\n"
            + references
            + "\n  </ItemGroup>\n"
            + "</Project>\n";
        string config =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<configuration>\n"
            + "  <packageSources>\n"
            + "    <clear />\n"
            + "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />\n"
            + "  </packageSources>\n"
            + "</configuration>\n";
        await File.WriteAllTextAsync(
            Path.Combine(root, "Conformance.csproj"),
            project,
            new UTF8Encoding(false),
            cancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "nuget.config"),
            config,
            new UTF8Encoding(false),
            cancellationToken
        );
    }

    private static async Task<Restore> RestoreAsync(
        string root,
        CancellationToken cancellationToken
    )
    {
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add("--nologo");
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using Process process =
            Process.Start(start)
            ?? throw new InvalidOperationException("dotnet restore could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new Restore(
            process.ExitCode,
            string.Concat(await output, Environment.NewLine, await error)
        );
    }

    private static async Task<bool> SdkAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo start = new("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("--version");
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private sealed record Restore(int ExitCode, string Output);
}
