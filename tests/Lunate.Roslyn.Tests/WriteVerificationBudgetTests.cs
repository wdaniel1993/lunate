using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;
using Lunate.TestSupport;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class WriteVerificationBudgetTests(ITestOutputHelper output)
{
    private const int WarmupCount = 2;
    private const int SampleCount = 7;
    private const int BudgetMilliseconds = 250;
    private const int MaximumBudgetMilliseconds = 10_000;
    private const double CalibratedMedianMilliseconds = 149.812;

    // Calibration: macOS 26.3.1 arm64, .NET SDK 10.0.402 / runtime 10.0.13;
    // direct-DLL Category=Perf run samples were 138.41, 84.32, 149.81, 107.33,
    // 274.33, 201.62, 342.78 ms; median 149.812 ms + 50% = 224.718 ms, rounded
    // up to the next 50 ms => 250 ms (below the 10000 ms cap).
    // Command: dotnet tests/Lunate.Roslyn.Tests/bin/Release/net10.0/Lunate.Roslyn.Tests.dll -class Lunate.Roslyn.Tests.WriteVerificationBudgetTests -showLiveOutput

    [Fact]
    [Trait("Category", "Perf")]
    public async Task WriteVerificationBudget()
    {
        using var fixture = Fixtures.CopySolution("write-verification-budget");
        RestoreFixture(fixture.Root);
        // Keep the historical .sln in the committed fixture; load its equivalent .slnx because the
        // workspace intentionally refuses an ambiguous pair of solution candidates.
        File.Delete(Path.Combine(fixture.Root, "Fixture.sln"));
        using var backend = new RoslynBackend(fixture.Root);
        var load = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.Loaded, load.Status);
        Assert.Equal(3, load.ProjectCount);
        Assert.Equal(16, load.DocumentCount);

        string relativePath = "src/Fixture.Lib/Calculator.cs";
        string absolutePath = Path.Combine(fixture.Root, relativePath);
        string baseline = File.ReadAllText(absolutePath);
        string staged = baseline + "\n// staged verification content\n";
        var verifier = new CSharpWriteVerifier(_ => backend);
        var request = new WriteVerificationRequest(
            relativePath,
            baseline,
            staged,
            new WorkspaceInfo(fixture.Root, null, null)
        );

        for (int index = 0; index < WarmupCount; index++)
        {
            WriteVerificationResult warmup = await verifier.VerifyAsync(
                request,
                CancellationToken.None
            );
            Assert.Equal(WriteVerificationStatus.Verified, warmup.Status);
        }

        var samples = new double[SampleCount];
        for (int index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            WriteVerificationResult result = await verifier.VerifyAsync(
                request,
                CancellationToken.None
            );
            stopwatch.Stop();
            Assert.Equal(WriteVerificationStatus.Verified, result.Status);
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        double median = samples.Order().ElementAt(samples.Length / 2);
        string sampleText = string.Join(
            ", ",
            samples.Select(sample => sample.ToString("F2", CultureInfo.InvariantCulture))
        );
        ChildProcessResult sdk = DotnetCli.Run(
            fixture.Root,
            ["--version"],
            new Dictionary<string, string?>(),
            TimeSpan.FromSeconds(10)
        );
        Assert.True(sdk.Succeeded, sdk.Describe("could not determine the .NET SDK version"));
        output.WriteLine(
            "calibration command: dotnet tests/Lunate.Roslyn.Tests/bin/Release/net10.0/Lunate.Roslyn.Tests.dll -class Lunate.Roslyn.Tests.WriteVerificationBudgetTests -showLiveOutput"
        );
        output.WriteLine(
            "gate command: dotnet test --project tests/Lunate.Roslyn.Tests/Lunate.Roslyn.Tests.csproj -c Release --no-build --filter-trait Category=Perf -nodeReuse:false"
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"environment: {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; .NET SDK {sdk.StandardOutput.Trim()}; runtime {Environment.Version}; DOTNET_ROOT={Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "(unset)"}"
            )
        );
        output.WriteLine(
            $"environment flags: MSBUILDDISABLENODEREUSE={Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE") ?? "(unset)"}; DOTNET_CLI_UI_LANGUAGE={Environment.GetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE") ?? "(unset)"}; PATH={Environment.GetEnvironmentVariable("PATH") ?? "(unset)"}"
        );
        output.WriteLine(
            $"workload: tests/fixtures/solutions/write-verification-budget; {load.ProjectCount} projects / {load.DocumentCount} Roslyn documents; workspace loaded before {WarmupCount} verifier warm-ups"
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"warm changed-file verifier samples ({samples.Length}, ms): {sampleText}; median: {median:F3} ms"
            )
        );
        double withHeadroom = CalibratedMedianMilliseconds * 1.5;
        int calibratedBudget = BudgetFor(CalibratedMedianMilliseconds);
        output.WriteLine(
            FormattableString.Invariant(
                $"calibration: recorded median {CalibratedMedianMilliseconds:F3} ms x 1.5 = {withHeadroom:F3} ms; rounded up in 50 ms increments => {calibratedBudget} ms; selected budget {BudgetMilliseconds} ms (maximum {MaximumBudgetMilliseconds} ms)"
            )
        );

        Assert.Equal(BudgetMilliseconds, calibratedBudget);
        Assert.Equal(MaximumBudgetMilliseconds, BudgetFor(MaximumBudgetMilliseconds));
        Assert.True(
            BudgetMilliseconds <= MaximumBudgetMilliseconds,
            $"the selected budget {BudgetMilliseconds} ms exceeds the absolute maximum of {MaximumBudgetMilliseconds} ms"
        );
        Assert.True(
            median <= BudgetMilliseconds,
            FormattableString.Invariant(
                $"warm changed-file verifier median {median:F3} ms exceeded the {BudgetMilliseconds} ms budget; samples: {sampleText}"
            )
        );
    }

    private static int BudgetFor(double medianMilliseconds) =>
        (int)Math.Min(MaximumBudgetMilliseconds, Math.Ceiling(medianMilliseconds * 1.5 / 50) * 50);

    private static void RestoreFixture(string root)
    {
        string? packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packages))
        {
            packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages"
            );
        }

        Directory.CreateDirectory(packages);
        string scratch = Path.Combine(root, ".tmp");
        Directory.CreateDirectory(scratch);
        File.WriteAllText(
            Path.Combine(root, "NuGet.config"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<configuration><packageSources><clear /></packageSources></configuration>"
        );
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_HOME"] = Path.Combine(root, ".dotnet-home"),
            ["NUGET_PACKAGES"] = packages,
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, ".nuget-http-cache"),
            ["TMPDIR"] = scratch,
            ["TMP"] = scratch,
            ["TEMP"] = scratch,
            ["MSBUILDUSESERVER"] = "0",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        };
        string solution = Path.Combine(root, "Fixture.slnx");
        ChildProcessResult restore = DotnetCli.Run(
            root,
            [
                "restore",
                solution,
                "--configfile",
                Path.Combine(root, "NuGet.config"),
                "--packages",
                packages,
                "-p:NuGetAudit=false",
                "-maxcpucount:1",
                "-nodeReuse:false",
                "--nologo",
            ],
            environment,
            TimeSpan.FromSeconds(120)
        );
        Assert.True(
            restore.Succeeded,
            restore.Describe("could not restore the write-verification fixture")
        );
    }
}
