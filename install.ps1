# Lunate installer for Windows (x64).
#
# Usage as a file:
#   ./install.ps1 [-Version <tag>] [-Prefix <dir>]
#
# One-liner (parameters come from the environment):
#   irm https://raw.githubusercontent.com/wdaniel1993/lunate/main/install.ps1 | iex
#
# Environment overrides:
#   LUNATE_INSTALL_BASE_URL  releases base URL
#                            (default: https://github.com/wdaniel1993/lunate/releases);
#                            the script downloads from <base>/latest/download
#                            or <base>/download/<tag>
#   LUNATE_INSTALL_VERSION   release tag to install, for example v0.1.0
#   LUNATE_INSTALL_PREFIX    install directory
#                            (default: %LOCALAPPDATA%\Programs\lunate)

param(
    [string]$Version,
    [string]$Prefix
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Save-RemoteFile {
    param([string]$Uri, [string]$OutFile)
    if ($Uri.StartsWith('file://')) {
        $local = ([uri]$Uri).LocalPath
        if (-not (Test-Path -LiteralPath $local)) {
            throw "install: not found: $local"
        }
        Copy-Item -LiteralPath $local -Destination $OutFile
    }
    else {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $OutFile
    }
}

$arch = $env:PROCESSOR_ARCHITECTURE
if ($arch -ne 'AMD64') {
    throw "install: unsupported architecture: $arch; lunate publishes a win-x64 build. On other platforms use install.sh"
}

if (-not $Version) { $Version = $env:LUNATE_INSTALL_VERSION }
if (-not $Prefix) { $Prefix = $env:LUNATE_INSTALL_PREFIX }
if (-not $Prefix) {
    if (-not $env:LOCALAPPDATA) {
        throw 'install: LOCALAPPDATA is not set; pass -Prefix <dir> or set LUNATE_INSTALL_PREFIX'
    }
    $Prefix = Join-Path $env:LOCALAPPDATA 'Programs\lunate'
}

$baseUrl = $env:LUNATE_INSTALL_BASE_URL
if (-not $baseUrl) {
    $baseUrl = 'https://github.com/wdaniel1993/lunate/releases'
}
if ($Version) {
    $tag = $Version
    if (-not $tag.StartsWith('v')) { $tag = "v$tag" }
    $rootUrl = "$baseUrl/download/$tag"
}
else {
    $rootUrl = "$baseUrl/latest/download"
}

$asset = 'lunate-win-x64.zip'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("lunate-install-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    Write-Host "installing lunate from $rootUrl"
    $archive = Join-Path $tmp $asset
    $sums = Join-Path $tmp 'SHA256SUMS'
    Save-RemoteFile -Uri "$rootUrl/$asset" -OutFile $archive
    Save-RemoteFile -Uri "$rootUrl/SHA256SUMS" -OutFile $sums

    $expected = $null
    foreach ($line in Get-Content -Path $sums) {
        $parts = $line.Trim() -split '\s+'
        if ($parts.Count -ge 2 -and $parts[1].TrimStart('*') -eq $asset) {
            $expected = $parts[0].ToLowerInvariant()
            break
        }
    }
    if (-not $expected) {
        throw "install: SHA256SUMS does not list $asset"
    }

    $actual = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "install: checksum mismatch for $asset (expected $expected, got $actual); nothing was installed"
    }

    $extract = Join-Path $tmp 'extract'
    Expand-Archive -Path $archive -DestinationPath $extract
    $exe = Join-Path $extract 'lunate.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "install: $asset does not contain lunate.exe"
    }

    New-Item -ItemType Directory -Force -Path $Prefix | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $Prefix 'lunate.exe') -Force

    $onPath = $false
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    foreach ($entry in @($userPath -split ';')) {
        if ($entry.Trim().TrimEnd('\') -ieq $Prefix.TrimEnd('\')) {
            $onPath = $true
            break
        }
    }
    if (-not $onPath) {
        $newPath = if ([string]::IsNullOrEmpty($userPath)) { $Prefix } else { $userPath.TrimEnd(';') + ';' + $Prefix }
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    }

    $installed = Join-Path $Prefix 'lunate.exe'
    $versionOutput = & $installed --version
    if ($LASTEXITCODE -ne 0) {
        throw "install: the installed lunate failed to run: $installed"
    }
    Write-Host "installed lunate $versionOutput to $installed"
    if (-not $onPath) {
        Write-Host "note: $Prefix was added to your user PATH; open a new terminal to use lunate"
    }
}
finally {
    Remove-Item -Recurse -Force -Path $tmp -ErrorAction SilentlyContinue
}
