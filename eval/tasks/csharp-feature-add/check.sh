#!/usr/bin/env bash
set -euo pipefail

reversed="$(dotnet run --project App/App.csproj -- --reverse one two three)"
printf '%s\n' "$reversed"
grep -qx "three two one" <<<"$reversed"

counted="$(dotnet run --project App/App.csproj -- one two three)"
printf '%s\n' "$counted"
grep -qx "words: 3" <<<"$counted"
