#!/usr/bin/env bash
set -euo pipefail

if grep -Rn "FormatName" Lib App; then
    echo "the old name FormatName is still present" >&2
    exit 1
fi

grep -Rn "FormatLabel" Lib App

dotnet build App/App.csproj
