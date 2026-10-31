# One retry mirrors scripts/verify.sh: record a transient pass, but fail a repeated test or
# discovery failure. The runner-path seam lets the Windows self-test exercise both outcomes.
function Add-VerifyFlake {
    param(
        [string]$Label,
        [string[]]$Lines
    )

    $first = $Lines | Where-Object {
        [string]$_ -match '^\s*(failed|xunit\.v3[^ ]* failed|\[FAIL\])'
    } | Select-Object -First 1
    $test = if ($first) {
        ([string]$first -replace '^\s*failed\s*', '').Trim()
    }
    else {
        'unknown'
    }
    $ledger = if ($env:LUNATE_FLAKE_LEDGER) {
        $env:LUNATE_FLAKE_LEDGER
    }
    else {
        Join-Path (Get-Location).Path '.artifacts/flake-ledger.jsonl'
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $ledger) | Out-Null
    $leg = if ($env:RUNNER_OS) { $env:RUNNER_OS.ToLowerInvariant() } else { 'windows' }
    $entry = [ordered]@{
        date = [DateTime]::UtcNow.ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
        leg  = $leg
        step = $Label
        test = $test
    }
    Add-Content -Path $ledger -Value ($entry | ConvertTo-Json -Compress) -Encoding utf8
    Write-Host "FLAKE: $Label — passed on retry: $test"
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "- flake: ${Label} (${test}) passed on retry" -Encoding utf8
    }
}

function Invoke-Tests {
    param(
        [string]$Label,
        [string[]]$TestArgs,
        [string]$RunnerPath = 'dotnet'
    )

    $attempt = 1
    $firstLines = @()
    while ($attempt -le 2) {
        $outFile = [System.IO.Path]::GetTempFileName()
        $errFile = [System.IO.Path]::GetTempFileName()
        try {
            $proc = Start-Process -FilePath $RunnerPath -ArgumentList $TestArgs -NoNewWindow -PassThru `
                -RedirectStandardOutput $outFile -RedirectStandardError $errFile
            $proc.WaitForExit()
            $exit = $proc.ExitCode
            $lines = @(Get-Content $outFile) + @(Get-Content $errFile)
            $lines | ForEach-Object { Write-Host $_ }

            $total = $null
            foreach ($line in $lines) {
                if ([string]$line -match 'total: (\d+)') { $total = [int]$Matches[1] }
            }
            if ($exit -eq 0 -and $null -ne $total -and $total -gt 0) {
                if ($attempt -eq 2) { Add-VerifyFlake -Label $Label -Lines $firstLines }
                return
            }

            if ($attempt -eq 1) {
                $firstLines = $lines
                $reason = if ($exit -ne 0) {
                    [string]::Format(
                        [System.Globalization.CultureInfo]::InvariantCulture,
                        '{0} failed once (exit {1})',
                        $Label,
                        $exit
                    )
                }
                else {
                    "$Label executed zero tests"
                }
                Write-Host "verify: $reason — retrying once under the flake budget"
                $attempt = 2
                continue
            }

            if ($exit -ne 0) {
                throw [string]::Format(
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    'verify: {0} failed twice in a row (exit {1}) — not a flake',
                    $Label,
                    $exit
                )
            }
            throw "verify: $Label executed zero tests after retry — test discovery regression?"
        }
        finally {
            Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
        }
    }
}
