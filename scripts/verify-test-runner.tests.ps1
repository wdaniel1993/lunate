$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'verify-test-runner.ps1')

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('lunate-verify-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
$fakeRunner = Join-Path $temp 'fake-runner.ps1'
$pwsh = (Get-Command pwsh -CommandType Application).Source
$names = @(
    'LUNATE_FLAKE_LEDGER',
    'GITHUB_STEP_SUMMARY',
    'VERIFY_TEST_MODE',
    'VERIFY_TEST_COUNTER'
)
$previous = @{}
foreach ($name in $names) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    @'
$mode = $env:VERIFY_TEST_MODE
$counter = $env:VERIFY_TEST_COUNTER
$attempt = if (Test-Path $counter) { [int](Get-Content $counter -Raw) + 1 } else { 1 }
Set-Content -Path $counter -Value $attempt -NoNewline
if ($mode -eq 'retry' -and $attempt -eq 1) {
    Write-Output 'failed Lunate.Coding.Tests.FakeFailure'
    Write-Output 'total: 1'
    exit 1
}
if ($mode -eq 'always-fail') {
    Write-Output 'failed Lunate.Coding.Tests.FakeFailure'
    Write-Output 'total: 1'
    exit 1
}
Write-Output 'total: 1'
exit 0
'@ | Set-Content -Path $fakeRunner -Encoding utf8NoBOM

    $env:LUNATE_FLAKE_LEDGER = Join-Path $temp 'flake-ledger.jsonl'
    $env:GITHUB_STEP_SUMMARY = Join-Path $temp 'summary.md'
    $env:VERIFY_TEST_MODE = 'retry'
    $env:VERIFY_TEST_COUNTER = Join-Path $temp 'retry-count'
    Invoke-Tests -Label 'self-test retry' -RunnerPath $pwsh -TestArgs @(
        '-NoProfile',
        '-File',
        ('"{0}"' -f $fakeRunner)
    )
    if ([int](Get-Content $env:VERIFY_TEST_COUNTER -Raw) -ne 2) {
        throw 'verify test runner self-test: a passing retry must run exactly twice'
    }
    $entry = Get-Content $env:LUNATE_FLAKE_LEDGER | ConvertFrom-Json
    if ($entry.step -ne 'self-test retry' -or $entry.test -notmatch 'FakeFailure') {
        throw 'verify test runner self-test: the passing retry was not recorded'
    }
    if ((Get-Content $env:GITHUB_STEP_SUMMARY -Raw) -notmatch 'passed on retry') {
        throw 'verify test runner self-test: the passing retry was not summarized'
    }

    $env:VERIFY_TEST_MODE = 'always-fail'
    $env:VERIFY_TEST_COUNTER = Join-Path $temp 'failure-count'
    $failedTwice = $false
    try {
        Invoke-Tests -Label 'self-test persistent failure' -RunnerPath $pwsh -TestArgs @(
            '-NoProfile',
            '-File',
            ('"{0}"' -f $fakeRunner)
        )
    }
    catch {
        $failedTwice = $_.Exception.Message -match 'failed twice in a row'
    }
    if (-not $failedTwice) {
        throw 'verify test runner self-test: a repeated failure must fail the gate'
    }
    if ([int](Get-Content $env:VERIFY_TEST_COUNTER -Raw) -ne 2) {
        throw 'verify test runner self-test: a persistent failure must run exactly twice'
    }
    if (@(Get-Content $env:LUNATE_FLAKE_LEDGER).Count -ne 1) {
        throw 'verify test runner self-test: a persistent failure must not be recorded as a flake'
    }

    Write-Host 'verify: PowerShell test-runner self-tests passed'
}
finally {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
    Remove-Item -Recurse -Force -Path $temp -ErrorAction SilentlyContinue
}
