#!/usr/bin/env bash
set -euo pipefail

output="$(dotnet run --project App/App.csproj)"
printf '%s\n' "$output"
grep -qx "Hello, Ada!" <<<"$output"
grep -qx "REPORT" <<<"$output"
