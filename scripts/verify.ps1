#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

Push-Location (Join-Path $PSScriptRoot '..')
try {
    $budgetMs = if ($env:BUDGET_MS) { [int]$env:BUDGET_MS } else { 150 }
    $configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { 'Release' }
    $publishDir = 'artifacts/publish'

    # English-first gate output: pin the dotnet CLI / test-runner UI language
    # (parity with scripts/verify.sh). Only UI strings are pinned, never cultures.
    if (-not $env:DOTNET_CLI_UI_LANGUAGE) { $env:DOTNET_CLI_UI_LANGUAGE = 'en' }

    function Measure-StartupMedian {
        param($Bin, $File)
        Write-Host "verify: benchmarking: $Bin --version"
        hyperfine --warmup 3 --runs 20 --export-json $File "$Bin --version" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'verify: hyperfine failed' }
        return [math]::Round(((Get-Content $File -Raw | ConvertFrom-Json).results[0].median) * 1000)
    }

    function Invoke-Tests {
        param([string]$Label, [string[]]$TestArgs)
        $output = & dotnet @TestArgs 2>&1
        $exit = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        if ($exit -ne 0) { throw "verify: $Label failed (exit $exit)" }
        # A discovery regression (runner or argument drift) must never look
        # green: the MTP summary line is "total: N"; missing or zero fails.
        $total = $null
        foreach ($line in $output) {
            if ([string]$line -match 'total: (\d+)') { $total = [int]$Matches[1] }
        }
        if ($null -eq $total -or $total -eq 0) {
            throw "verify: $Label executed zero tests - test discovery regression?"
        }
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
    # The Category=Perf budget check is excluded from the main pass and runs as
    # its own step below, so the pass stays fast and the budget is still enforced.
    Invoke-Tests -Label 'test' -TestArgs @('test', '--solution', 'lunate.sln', '-c', $configuration, '--filter-not-trait', 'Category=Perf')

    Write-Host "`n==> path index budget"
    Invoke-Tests -Label 'path index budget' -TestArgs @('test', '--project', 'tests/Lunate.Coding.Tests/Lunate.Coding.Tests.csproj', '-c', $configuration, '--no-build', '--filter-trait', 'Category=Perf')

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

    Write-Host "`n==> install script (fixture release)"
    $version = [regex]::Match((Get-Content Directory.Build.props -Raw), '<Version>(.*?)</Version>').Groups[1].Value
    if (-not $version) { throw 'verify: cannot read <Version> from Directory.Build.props' }

    $installTmp = Join-Path ([System.IO.Path]::GetTempPath()) ('lunate-verify-install-' + [guid]::NewGuid().ToString('N'))
    $fixture = Join-Path $installTmp 'fixture'
    $latestDir = Join-Path $fixture 'latest/download'
    $pinnedDir = Join-Path $fixture "download/v$version"
    $prefix = Join-Path $installTmp 'prefix'
    $prefixFlags = Join-Path $installTmp 'prefix-flags'
    New-Item -ItemType Directory -Force -Path $latestDir, $pinnedDir, $prefix, $prefixFlags | Out-Null
    # The fixture installs append GUID temp dirs to the user PATH; snapshot it so
    # the finally block restores it instead of leaving dangling entries behind.
    $userPathBefore = [Environment]::GetEnvironmentVariable('Path', 'User')
    try {
        $zipName = 'lunate-win-x64.zip'
        $zip = Join-Path $latestDir $zipName
        Compress-Archive -Path (Join-Path $publishDir "$env:RID/lunate.exe") -DestinationPath $zip
        $hash = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
        $sumsLine = "$hash  $zipName"
        Set-Content -Path (Join-Path $latestDir 'SHA256SUMS') -Value $sumsLine -Encoding ascii
        Copy-Item -Path $zip -Destination (Join-Path $pinnedDir $zipName)
        Set-Content -Path (Join-Path $pinnedDir 'SHA256SUMS') -Value $sumsLine -Encoding ascii

        # (a) Default install: the environment supplies only the releases base.
        $env:LUNATE_INSTALL_BASE_URL = 'file:///' + ($fixture -replace '\\', '/')
        $env:LUNATE_INSTALL_PREFIX = $prefix
        Remove-Item Env:LUNATE_INSTALL_VERSION -ErrorAction SilentlyContinue
        & ./install.ps1
        $published = & $binaryFull --version
        $installed = & (Join-Path $prefix 'lunate.exe') --version
        if ($installed -ne $published) {
            throw "verify: installed lunate reports $installed, expected $published"
        }

        # (b) -Version/-Prefix must drive the composed URL and the target directory.
        Remove-Item Env:LUNATE_INSTALL_PREFIX -ErrorAction SilentlyContinue
        & ./install.ps1 -Version "v$version" -Prefix $prefixFlags
        $installedFlags = & (Join-Path $prefixFlags 'lunate.exe') --version
        if ($installedFlags -ne $published) {
            throw "verify: installed lunate reports $installedFlags (flags), expected $published"
        }

        # (c) A tag with no release directory must fail even though latest/ exists:
        # the tag participates in the URL the script composes.
        $badTag = $false
        try {
            & ./install.ps1 -Version 'v9.9.9' -Prefix (Join-Path $installTmp 'prefix-bad-tag')
        }
        catch { $badTag = $true }
        if (-not $badTag) { throw 'verify: install.ps1 installed a nonexistent version v9.9.9 from the fixture' }

        $badFixture = Join-Path $installTmp 'bad-fixture'
        Copy-Item -Recurse -Path $fixture -Destination $badFixture
        [System.IO.File]::AppendAllText((Join-Path $badFixture "latest/download/$zipName"), 'corrupt')
        $badPrefix = Join-Path $installTmp 'bad-prefix'
        New-Item -ItemType Directory -Force -Path $badPrefix | Out-Null
        $env:LUNATE_INSTALL_BASE_URL = 'file:///' + ($badFixture -replace '\\', '/')
        $env:LUNATE_INSTALL_PREFIX = $badPrefix
        $aborted = $false
        try { & ./install.ps1 } catch { $aborted = $true }
        if (-not $aborted) { throw 'verify: install.ps1 accepted a checksum mismatch' }
        if (Test-Path (Join-Path $badPrefix 'lunate.exe')) {
            throw 'verify: install.ps1 left a partial install after a checksum mismatch'
        }

        # Architecture: ARM64 (either variable) is refused; the WOW64 edge
        # (32-bit PowerShell on x64) must install.
        $previousArch = $env:PROCESSOR_ARCHITECTURE
        $previousArchW6432 = $env:PROCESSOR_ARCHITEW6432
        try {
            $env:PROCESSOR_ARCHITECTURE = 'ARM64'
            $env:PROCESSOR_ARCHITEW6432 = 'ARM64'
            $refusal = ''
            try { & ./install.ps1 } catch { $refusal = $_.Exception.Message }
            if ($refusal -notmatch 'win-x64') {
                throw "verify: install.ps1 did not refuse ARM64: $refusal"
            }

            $env:LUNATE_INSTALL_BASE_URL = 'file:///' + ($fixture -replace '\\', '/')
            $env:PROCESSOR_ARCHITECTURE = 'x86'
            $env:PROCESSOR_ARCHITEW6432 = 'AMD64'
            # The trailing slash must match the existing PATH entry from (a):
            # no duplicate entry, no false 'added to PATH' hint.
            & ./install.ps1 -Prefix ($prefix + '/')
            $wow64 = & (Join-Path $prefix 'lunate.exe') --version
            if ($wow64 -ne $published) {
                throw "verify: WOW64 install reports $wow64, expected $published"
            }
        }
        finally {
            $env:PROCESSOR_ARCHITECTURE = $previousArch
            if ($null -eq $previousArchW6432) {
                Remove-Item Env:PROCESSOR_ARCHITEW6432 -ErrorAction SilentlyContinue
            }
            else {
                $env:PROCESSOR_ARCHITEW6432 = $previousArchW6432
            }
        }

        $entries = @(([Environment]::GetEnvironmentVariable('Path', 'User')) -split ';')
        $normalizedPrefix = ($prefix -replace '/', '\').TrimEnd('\')
        $pathMatches = @($entries | Where-Object { $_.Trim().Replace('/', '\').TrimEnd('\') -ieq $normalizedPrefix })
        if ($pathMatches.Count -ne 1) {
            throw "verify: the user PATH holds $($pathMatches.Count) entries for $prefix; expected 1 (separators normalize)"
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('Path', $userPathBefore, 'User')
        Remove-Item Env:LUNATE_INSTALL_BASE_URL -ErrorAction SilentlyContinue
        Remove-Item Env:LUNATE_INSTALL_PREFIX -ErrorAction SilentlyContinue
        Remove-Item Env:LUNATE_INSTALL_VERSION -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force -Path $installTmp -ErrorAction SilentlyContinue
    }

    Write-Host "`n==> tool package roundtrip"
    $toolTmp = Join-Path ([System.IO.Path]::GetTempPath()) ('lunate-verify-tool-' + [guid]::NewGuid().ToString('N'))
    $feed = Join-Path $toolTmp 'feed'
    $toolPath = Join-Path $toolTmp 'tools'
    New-Item -ItemType Directory -Force -Path $feed | Out-Null
    try {
        dotnet pack src/Lunate.Coding/Lunate.Coding.csproj -c $configuration -o $feed --nologo
        if ($LASTEXITCODE -ne 0) { throw 'verify: dotnet pack failed' }
        dotnet tool install --tool-path $toolPath --add-source $feed lunate --version $version
        if ($LASTEXITCODE -ne 0) { throw 'verify: dotnet tool install failed' }
        $toolVersion = & (Join-Path $toolPath 'lunate.exe') --version
        if (($toolVersion -split '\+')[0] -ne $version) {
            throw "verify: tool roundtrip reports $toolVersion, expected $version"
        }
    }
    finally {
        Remove-Item -Recurse -Force -Path $toolTmp -ErrorAction SilentlyContinue
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
    # Edits and deletions of existing Shipped files fail; a newly added
    # tracking file is the bootstrap path and does not trip the gate.
    git diff --exit-code --diff-filter=MD $base -- '*PublicAPI.Shipped.txt'
    if ($LASTEXITCODE -ne 0) { throw "verify: PublicAPI.Shipped.txt changed relative to $base" }

    Write-Host 'verify: OK'
}
finally {
    Pop-Location
}
