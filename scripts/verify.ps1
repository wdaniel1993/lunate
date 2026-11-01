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
    $env:MSBUILDDISABLENODEREUSE = '1'

    # Stale scratch sweep (parity with scripts/verify.sh): a hard-killed run can
    # leave a lunate-verify-* dir in the temp path; remove ones older than 4
    # hours (a live gate run never lasts that long).
    Get-ChildItem -Path ([System.IO.Path]::GetTempPath()) -Directory -Filter 'lunate-verify-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt (Get-Date).AddHours(-4) } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    # The gate is the FLOOR, the fastest of the runs, with the median reported beside it
    # (ADR-0005 addendum, 2026-10-25): interference on a shared runner only ever adds
    # time, so the fastest run estimates the binary and the median estimates the machine.
    function Measure-Startup {
        param($Bin, $File)
        Write-Host "verify: benchmarking: $Bin --version"
        hyperfine --warmup 3 --runs 20 --export-json $File "$Bin --version" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'verify: hyperfine failed' }
        # Two JSON shapes are in the wild (hyperfine 2.x writes the statistics under
        # .results[].summary.time_wall_clock, 1.x writes .min/.median at the top level), and
        # StrictMode makes a missing property an error — ask the object which properties it
        # has instead of assuming either. A shape that fits neither throws: a gate that cannot
        # read its measurement must fail, never pass with an empty one.
        $result = (Get-Content $File -Raw | ConvertFrom-Json).results[0]
        $wall = if ($result.PSObject.Properties['summary']) {
            $result.summary.time_wall_clock
        } else {
            $result
        }
        $floor = if ($wall.PSObject.Properties['min']) {
            $wall.min
        } elseif ($result.PSObject.Properties['times']) {
            ($result.times | Measure-Object -Minimum).Minimum
        } else {
            $null
        }
        $median = if ($wall.PSObject.Properties['median']) {
            $wall.median
        } elseif ($result.PSObject.Properties['median']) {
            $result.median
        } else {
            $null
        }
        if ($null -eq $floor -or $null -eq $median) {
            throw 'verify: the hyperfine JSON carries no wall-clock timing'
        }
        return [pscustomobject]@{
            Floor  = [math]::Round($floor * 1000)
            Median = [math]::Round($median * 1000)
        }
    }

    function Invoke-Tests {
        param([string]$Label, [string[]]$TestArgs)
        # Output goes to files via Start-Process (raw handle redirection), not
        # through a PowerShell pipeline: a pipeline ends only at EOF, and EOF
        # needs every writer of the pipe to close; build workers inherit the
        # pipe and can outlive the command by design, stalling the step for
        # minutes on Windows CI (probe-verified; parity with scripts/verify.sh).
        # File handles have no such dependency: the step ends when the command
        # ends. The trade-off is that the pass prints after it runs.
        $outFile = [System.IO.Path]::GetTempFileName()
        $errFile = [System.IO.Path]::GetTempFileName()
        $proc = Start-Process -FilePath 'dotnet' -ArgumentList $TestArgs -NoNewWindow -PassThru `
            -RedirectStandardOutput $outFile -RedirectStandardError $errFile
        $proc.WaitForExit()
        $exit = $proc.ExitCode
        $lines = @(Get-Content $outFile) + @(Get-Content $errFile)
        $lines | ForEach-Object { Write-Host $_ }
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
        if ($exit -ne 0) {
            throw "verify: $Label failed (exit $exit)"
        }
        # A discovery regression (runner or argument drift) must never look
        # green: the MTP summary line is "total: N"; missing or zero fails.
        $total = $null
        foreach ($line in $lines) {
            if ([string]$line -match 'total: (\d+)') { $total = [int]$Matches[1] }
        }
        if ($null -eq $total -or $total -eq 0) {
            throw "verify: $Label executed zero tests - test discovery regression?"
        }
    }

    # Git Bash is the bash Windows has, and the runner does not always put it on PATH: its own
    # `shell: bash` resolves the executable from the Git installation, so do the same.
    function Resolve-Bash {
        $onPath = Get-Command bash -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($onPath) { return $onPath.Source }
        foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
            if (-not $root) { continue }
            $candidate = Join-Path $root 'Git/bin/bash.exe'
            if (Test-Path $candidate) { return $candidate }
        }
        throw 'verify: bash is required to run scripts/check-package-metadata.sh; install Git Bash (Windows) or run the bash gate'
    }

    # Fails when the packed .nupkg cannot render a complete feed page (licence, readme,
    # repository). The rule lives in one script so bash and PowerShell cannot disagree about what
    # "complete metadata" means: 0.1.0 shipped with "License missing" and "Readme missing", and an
    # inline copy here read the intermediate nuspec in obj/, which pack writes differently on
    # Windows — the Windows legs went red for a package that was fine (PR #235).
    function Assert-PackageMetadata {
        param([string]$Package, [string]$PackageId, [switch]$Prerelease)
        if (-not (Test-Path $Package)) {
            throw "verify: no package at $Package (dotnet pack writes <id>.<version>.nupkg to the -o directory)"
        }
        # Git Bash reads a native path in its own dialect best.
        $native = ($Package -replace '\\', '/')
        $checkArgs = @($native, '--id', $PackageId)
        if ($Prerelease) { $checkArgs += '--prerelease' }
        & (Resolve-Bash) (Join-Path $PSScriptRoot 'check-package-metadata.sh') @checkArgs
        if ($LASTEXITCODE -ne 0) {
            throw "verify: $Package does not carry the package metadata a feed page needs"
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
    dotnet build lunate.slnx -c $configuration --nologo
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

    # Publish and measure the startup budget BEFORE the test suite: the same gate, taken
    # on a quiet machine. Measured after fifteen minutes of tests, the median is reading
    # the runner's thermal and scheduler state, not the product (ADR-0005 keeps the budget).
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

    $startup = Measure-Startup -Bin $binaryFull -File $resultsFile
    Write-Host "startup: floor $($startup.Floor) ms (median $($startup.Median) ms, budget $budgetMs ms)"
    # Noise policy (ADR-0005, mirrors scripts/perf.sh): a single budget miss re-runs
    # the measurement once before failing, so a shared-runner noise burst does not
    # redden the gate while a material regression still misses both attempts.
    if ($startup.Floor -gt $budgetMs) {
        Write-Host "startup floor $($startup.Floor) ms exceeds budget $budgetMs ms; re-running once (noise policy)"
        $startup = Measure-Startup -Bin $binaryFull -File $resultsFile
        Write-Host "startup: floor $($startup.Floor) ms (median $($startup.Median) ms, budget $budgetMs ms; retry)"
    }
    if ($startup.Floor -gt $budgetMs) {
        Write-Error "startup budget exceeded: fastest of 20 runs $($startup.Floor) ms > $budgetMs ms (median $($startup.Median) ms)" -ErrorAction Continue
        exit 1
    }

    Write-Host "`n==> test"
    # The Category=Perf budget check is excluded from the main pass and runs as
    # its own step below, so the pass stays fast and the budget is still enforced.
    Invoke-Tests -Label 'test' -TestArgs @('test', '--solution', 'lunate.slnx', '-c', $configuration, '--filter-not-trait', 'Category=Perf')

    Write-Host "`n==> path index budget"
    Invoke-Tests -Label 'path index budget' -TestArgs @('test', '--project', 'tests/Lunate.Coding.Tests/Lunate.Coding.Tests.csproj', '-c', $configuration, '--no-build', '--filter-trait', 'Category=Perf')

    Write-Host "`n==> write verifier budget"
    Invoke-Tests -Label 'write verifier budget' -TestArgs @('test', '--project', 'tests/Lunate.Roslyn.Tests/Lunate.Roslyn.Tests.csproj', '-c', $configuration, '--no-build', '--filter-trait', 'Category=Perf')

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
        Assert-PackageMetadata -Package (Join-Path $feed "lunate.$version.nupkg") -PackageId 'lunate'
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

    Write-Host "`n==> extension package roundtrip"
    # The release ships an extension package beside the tool, so the bundle `dotnet pack` writes is a
    # product artifact, not a test fixture. Pack the sample, install it from a folder feed with the
    # published binary, verify the record and uninstall — the same cycle the release smoke runs
    # against nuget.org, so a layout change fails on every commit instead of at a tag.
    $packageTmp = Join-Path ([System.IO.Path]::GetTempPath()) ('lunate-verify-package-' + [guid]::NewGuid().ToString('N'))
    $packageFeed = Join-Path $packageTmp 'feed'
    $packageTree = Join-Path $packageTmp 'worktree'
    $packageHome = Join-Path $packageTmp 'home'
    New-Item -ItemType Directory -Force -Path $packageFeed, $packageTree, $packageHome | Out-Null
    try {
        dotnet pack extensions/subagents/SubagentsExtension/SubagentsExtension.csproj -c $configuration -o $packageFeed --nologo
        if ($LASTEXITCODE -ne 0) { throw 'verify: dotnet pack of the extension sample failed' }
        Assert-PackageMetadata `
            -Package (Join-Path $packageFeed "Lunate.Extensions.Subagents.$version.nupkg") `
            -PackageId 'Lunate.Extensions.Subagents'
        $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$($packageFeed -replace '\\', '/')" />
  </packageSources>
  <config>
    <add key="globalPackagesFolder" value="$((Join-Path $packageTmp 'packages') -replace '\\', '/')" />
  </config>
</configuration>
"@
        Set-Content -Path (Join-Path $packageTree 'nuget.config') -Value $nugetConfig -Encoding utf8
        $package = 'Lunate.Extensions.Subagents'
        # $binary is relative to the repository root, and the cycle below runs from $packageTree
        # so the folder feed's nuget.config is found: resolve it first, or cmd cannot find the exe.
        $packageBinary = (Resolve-Path $binary).Path
        Push-Location $packageTree
        try {
            $env:LUNATE_HOME = $packageHome
            & $packageBinary install "$package@$version"
            if ($LASTEXITCODE -ne 0) { throw 'verify: lunate install of the extension sample failed' }
            & $packageBinary packages --verify
            if ($LASTEXITCODE -ne 0) { throw 'verify: lunate packages --verify failed for the extension sample' }
            & $packageBinary uninstall $package
            if ($LASTEXITCODE -ne 0) { throw 'verify: lunate uninstall of the extension sample failed' }
            if (Test-Path (Join-Path $packageHome 'extensions/subagents')) {
                throw 'verify: the extension package roundtrip left the extension behind'
            }
            if (Get-ChildItem -Path $packageHome -Filter install.json -Recurse -ErrorAction SilentlyContinue) {
                throw 'verify: the extension package roundtrip left an install record behind'
            }
        }
        finally {
            Pop-Location
            Remove-Item Env:LUNATE_HOME -ErrorAction SilentlyContinue
        }
    }
    finally {
        Remove-Item -Recurse -Force -Path $packageTmp -ErrorAction SilentlyContinue
    }

    Write-Host "`n==> contract package roundtrip"
    # The extension contract ships beside the tool (#237), on the same tag and no looser: the four
    # SDK packages are checked by the same metadata script (the preview pair under the prerelease
    # flag), then the template's NuGet shape is proven against this feed — the packages nuget.org
    # serves after the next tag, exercised on every commit where they exist only in dist/.
    # Parity with scripts/verify.sh's contract package roundtrip; the rule set lives in one
    # script so bash and PowerShell cannot disagree.
    $contractTmp = Join-Path ([System.IO.Path]::GetTempPath()) ('lunate-verify-contract-' + [guid]::NewGuid().ToString('N'))
    $contractFeed = Join-Path $contractTmp 'feed'
    New-Item -ItemType Directory -Force -Path $contractFeed | Out-Null
    try {
        foreach ($contractProject in @(
            'src/Lunate.Contracts/Lunate.Contracts.csproj',
            'src/Lunate.Extensibility.Abstractions/Lunate.Extensibility.Abstractions.csproj',
            'src/Lunate.Agent/Lunate.Agent.csproj',
            'src/Lunate.Extensibility.Testing/Lunate.Extensibility.Testing.csproj'
        )) {
            dotnet pack $contractProject -c $configuration -o $contractFeed --nologo -nodeReuse:false
            if ($LASTEXITCODE -ne 0) { throw "verify: dotnet pack failed for $contractProject" }
        }
        # The stable pair is versioned with ExtensionApi, the preview pair 0.2.0-preview.<n> —
        # neither is the repository <Version>, so each is read from the csproj that owns it.
        $contractCsproj = Get-Content 'src/Lunate.Extensibility.Abstractions/Lunate.Extensibility.Abstractions.csproj' -Raw
        $previewCsproj = Get-Content 'src/Lunate.Agent/Lunate.Agent.csproj' -Raw
        $contractVersion = [regex]::Match($contractCsproj, '<Version>([^<]+)</Version>').Groups[1].Value
        $previewVersion = [regex]::Match($previewCsproj, '<Version>([^<]+)</Version>').Groups[1].Value
        Assert-PackageMetadata -Package (Join-Path $contractFeed "Lunate.Contracts.$contractVersion.nupkg") -PackageId 'Lunate.Contracts'
        Assert-PackageMetadata -Package (Join-Path $contractFeed "Lunate.Extensibility.Abstractions.$contractVersion.nupkg") -PackageId 'Lunate.Extensibility.Abstractions'
        Assert-PackageMetadata -Package (Join-Path $contractFeed "Lunate.Agent.$previewVersion.nupkg") -PackageId 'Lunate.Agent' -Prerelease
        Assert-PackageMetadata -Package (Join-Path $contractFeed "Lunate.Extensibility.Testing.$previewVersion.nupkg") -PackageId 'Lunate.Extensibility.Testing' -Prerelease
        # Restore and build the child extension directly too: the test project's after-build target
        # invokes it as a second project, whose assets file would otherwise still describe the
        # earlier in-repository ProjectReference shape.
        dotnet restore templates/extension/TemplateExtension/TemplateExtension.csproj -p:LunateUseNuGetContracts=true -p:RestoreAdditionalProjectSources="$contractFeed" --nologo -nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw 'verify: contract feed restore of the template extension failed' }
        dotnet build templates/extension/TemplateExtension/TemplateExtension.csproj -c $configuration -p:LunateUseNuGetContracts=true --nologo -nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw 'verify: the template NuGet build failed' }
        dotnet restore templates/extension/TemplateExtension.Tests/TemplateExtension.Tests.csproj -p:LunateUseNuGetContracts=true -p:RestoreAdditionalProjectSources="$contractFeed" --nologo -nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw 'verify: contract feed restore of the template failed' }
        dotnet build templates/extension/TemplateExtension.Tests/TemplateExtension.Tests.csproj -c $configuration -p:LunateUseNuGetContracts=true --nologo -nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw 'verify: the template NuGet build failed' }
    }
    finally {
        Remove-Item -Recurse -Force -Path $contractTmp -ErrorAction SilentlyContinue
    }

    Write-Host "`n==> format"
    # CSharpier owns formatting; dotnet format keeps style and analyzer duties.
    dotnet csharpier check .
    if ($LASTEXITCODE -ne 0) { throw 'verify: format check failed (dotnet csharpier check .)' }

    dotnet format style lunate.slnx --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'verify: style check failed' }

    dotnet format analyzers lunate.slnx --verify-no-changes --no-restore
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
