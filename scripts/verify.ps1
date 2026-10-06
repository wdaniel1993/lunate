#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

Push-Location (Join-Path $PSScriptRoot '..')
try {
    $budgetMs = if ($env:BUDGET_MS) { [int]$env:BUDGET_MS } else { 150 }
    $configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { 'Release' }
    $publishDir = 'artifacts/publish'

    function Measure-StartupMedian {
        param($Bin, $File)
        Write-Host "verify: benchmarking: $Bin --version"
        hyperfine --warmup 3 --runs 20 --export-json $File "$Bin --version" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'verify: hyperfine failed' }
        return [math]::Round(((Get-Content $File -Raw | ConvertFrom-Json).results[0].median) * 1000)
    }

    if (-not $env:RID) {
        $arch = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
            'X64' { 'x64' }
            'Arm64' { 'arm64' }
            default { throw "verify: unsupported architecture: $_; set RID explicitly, for example RID=win-x64" }
        }
        $env:RID = "win-$arch"
    }

    Write-Host "`n==> tools"
    # CSharpier is pinned in .config/dotnet-tools.json; restore makes it available locally.
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw 'verify: dotnet tool restore failed; run it from the repository root to install CSharpier (pinned in .config/dotnet-tools.json)'
    }
    dotnet csharpier --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'verify: csharpier not found; install it with: dotnet tool restore' }

    Write-Host "`n==> build"
    dotnet build lunate.sln -c $configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'verify: build failed' }

    Write-Host "`n==> provider runtime assets"
    # Providers are compile-private to Lunate.Ai, but their runtime assets must reach
    # the app output or provider construction fails at run time.
    foreach ($assembly in @('Anthropic', 'OpenAI', 'Microsoft.Extensions.AI')) {
        $asset = "src/Lunate.Coding/bin/$configuration/net10.0/${assembly}.dll"
        if (-not (Test-Path $asset)) {
            throw "verify: ${assembly}.dll is missing from the Lunate.Coding build output"
        }
    }

    Write-Host "`n==> test"
    dotnet test --solution lunate.sln -c $configuration
    if ($LASTEXITCODE -ne 0) { throw 'verify: tests failed' }

    $binary = Join-Path (Join-Path $publishDir $env:RID) 'lunate.exe'
    # ADR-0008: release builds are single file + ReadyToRun WITHOUT compression
    # (compression crashed on macOS and costs startup time; archives are
    # compressed instead). Kept aligned with scripts/verify.sh and release.yml.
    Write-Host "`n==> publish ($env:RID)"
    dotnet publish src/Lunate.Coding/Lunate.Coding.csproj `
        -c $configuration `
        -r $env:RID `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true `
        -o (Join-Path $publishDir $env:RID) `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw 'verify: publish failed' }

    Write-Host "`n==> startup budget"
    $resultsFile = 'artifacts/perf.json'
    New-Item -ItemType Directory -Force -Path (Split-Path $resultsFile) | Out-Null
    # hyperfine runs commands through cmd.exe on Windows, which cannot execute quoted
    # relative paths; pass the absolute Windows path instead.
    $binaryFull = (Resolve-Path $binary).Path
    & $binaryFull --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "verify: binary failed to run: $binaryFull" }

    $medianMs = Measure-StartupMedian -Bin $binaryFull -File $resultsFile
    Write-Host "startup median: $medianMs ms (budget $budgetMs ms)"
    # Noise policy (ADR-0005, mirrors scripts/perf.sh): a single budget miss re-runs
    # the measurement once before failing, so a shared-runner noise burst does not
    # redden the gate while a material regression still misses both attempts.
    if ($medianMs -gt $budgetMs) {
        Write-Host "startup median $medianMs ms exceeds budget $budgetMs ms; re-running once (noise policy)"
        $medianMs = Measure-StartupMedian -Bin $binaryFull -File $resultsFile
        Write-Host "startup median (retry): $medianMs ms (budget $budgetMs ms)"
    }
    if ($medianMs -gt $budgetMs) {
        Write-Error "startup budget exceeded: $medianMs ms > $budgetMs ms (two consecutive measurements)" -ErrorAction Continue
        exit 1
    }

    Write-Host "`n==> format"
    # CSharpier owns formatting; dotnet format keeps style and analyzer duties.
    dotnet csharpier check .
    if ($LASTEXITCODE -ne 0) { throw 'verify: format check failed (dotnet csharpier check .)' }

    dotnet format style lunate.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'verify: style check failed' }

    dotnet format analyzers lunate.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'verify: analyzers check failed' }

    Write-Host "`n==> docs lint"
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        throw 'verify: Node.js is required for the documentation lint step; install Node.js (npx runs the pinned markdownlint-cli2)'
    }
    npx --yes markdownlint-cli2@0.23.3
    if ($LASTEXITCODE -ne 0) { throw 'verify: documentation lint failed' }

    Write-Host "`n==> public API"
    # Compare against the merge base with origin/main, then main, then HEAD
    # (working tree only) when neither exists.
    $base = git merge-base HEAD origin/main 2>$null
    if ($LASTEXITCODE -ne 0) {
        $base = git merge-base HEAD main 2>$null
    }
    if ($LASTEXITCODE -ne 0) {
        $base = 'HEAD'
        [Console]::Error.WriteLine('note: no main ref found; comparing the working tree only')
    }
    git diff --exit-code $base -- '*PublicAPI.Shipped.txt'
    if ($LASTEXITCODE -ne 0) { throw "verify: PublicAPI.Shipped.txt changed relative to $base" }

    Write-Host 'verify: OK'
}
finally {
    Pop-Location
}
