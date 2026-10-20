#:project ../src/Lunate.Ai/Lunate.Ai.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

// File-based apps disable reflection-based JSON by default; ProviderPresets uses it.

using System.Globalization;
using System.Text;
using Lunate.Ai;

string? snapshotPath = null;
string outputPath = "src/Lunate.Ai/models.json";
bool check = false;

for (int index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--snapshot" when index + 1 < args.Length:
            snapshotPath = args[++index];
            break;
        case "--output" when index + 1 < args.Length:
            outputPath = args[++index];
            break;
        case "--check":
            check = true;
            break;
        default:
            Console.Error.WriteLine($"error: unknown argument '{args[index]}'");
            PrintUsage();
            return 2;
    }
}

if (snapshotPath is null)
{
    PrintUsage();
    return 2;
}

if (!File.Exists(snapshotPath))
{
    Console.Error.WriteLine($"error: snapshot '{snapshotPath}' does not exist.");
    return 2;
}

CatalogSnapshot snapshot = ModelsDevCatalog.Generate(
    File.ReadAllText(snapshotPath),
    ProviderPresets.All
);
string fullOutputPath = Path.GetFullPath(outputPath);
byte[] generated = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(snapshot.Json);

if (check)
{
    byte[] current = File.Exists(fullOutputPath) ? File.ReadAllBytes(fullOutputPath) : [];
    if (!current.AsSpan().SequenceEqual(generated))
    {
        Console.Error.WriteLine(
            $"error: {fullOutputPath} is out of date; run scripts/update-models.sh to regenerate it."
        );
        return 1;
    }

    Console.WriteLine($"{fullOutputPath} is up to date.");
}
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
    File.WriteAllBytes(fullOutputPath, generated);
    Console.WriteLine($"wrote {fullOutputPath}");
}

Console.WriteLine($"models: {snapshot.Models.Count.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"sha256: {snapshot.SourceSha256}");
return 0;

static void PrintUsage() =>
    Console.Error.WriteLine(
        "usage: dotnet run scripts/update-models.cs -- --snapshot <path> [--output <path>] [--check]"
    );
