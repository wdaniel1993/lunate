#!/usr/bin/env bash
# S-4 reproducible measurement runner (throwaway spike).
# Usage: bash docs/spikes/S-4/run.sh
# Captures evidence under docs/spikes/S-4/evidence/. Large-solution steps are
# documented in report.md and run manually (clone is gitignored).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

PROBE_SRC=docs/spikes/S-4/WorkspaceProbe/WorkspaceProbe.csproj
BIN=docs/spikes/S-4/WorkspaceProbe/bin/Release/net10.0/WorkspaceProbe
FIXTURE=docs/spikes/S-4/fixture/Fixture.sln
EVIDENCE=docs/spikes/S-4/evidence
SCRATCH=artifacts/s4-scratch

export MSBUILDDISABLENODEREUSE=1
mkdir -p "$EVIDENCE" "$SCRATCH"

echo "== build =="
dotnet build "$PROBE_SRC" -c Release

echo "== self-test =="
"$BIN" selftest | tee "$EVIDENCE/selftest.txt"

echo "== locate =="
"$BIN" locate | tee "$EVIDENCE/locate.txt"

echo "== fixture restore + cold loads =="
dotnet restore "$FIXTURE"
{
  for i in 1 2 3; do
    echo "=== fixture cold run $i ==="
    /usr/bin/time -p "$BIN" load "$FIXTURE" --diagnostics --label fixture
  done
} | tee "$EVIDENCE/fixture-load.txt"

echo "== fixture edit-to-diagnostics =="
"$BIN" load "$FIXTURE" --file "Fixture.Lib/Calculator.cs" --edit-iterations 6 \
  --label fixture-edit | tee "$EVIDENCE/fixture-edit.txt"

echo "== fixture failure modes =="
rm -rf "$SCRATCH/fixture-norestore" "$SCRATCH/fixture-brokenref"
rsync -a --exclude bin --exclude obj docs/spikes/S-4/fixture/ "$SCRATCH/fixture-norestore/"
"$BIN" load "$SCRATCH/fixture-norestore/Fixture.sln" --diagnostics \
  --label fixture-norestore | tee "$EVIDENCE/fixture-norestore.txt"

echo "== single-file publish (plain; build host bundled, expected to fail) =="
dotnet publish "$PROBE_SRC" -c Release -r osx-arm64 --self-contained false \
  -p:PublishSingleFile=true -p:KeepBuildHostLoose=false -o "$SCRATCH/single-file"

echo "== plain single-file load (expected to fail) =="
if "$SCRATCH/single-file/WorkspaceProbe" load "$FIXTURE" --label single-file-plain >"$EVIDENCE/single-file-plain-load.txt" 2>&1; then
  echo "UNEXPECTED: plain single-file load succeeded" >&2
  exit 1
fi
echo "expected failure observed (evidence/single-file-plain-load.txt)"

echo "== single-file publish (build host kept loose) =="
dotnet publish "$PROBE_SRC" -c Release -r osx-arm64 --self-contained false \
  -p:PublishSingleFile=true -o "$SCRATCH/single-file-workaround"

"$SCRATCH/single-file-workaround/WorkspaceProbe" load "$FIXTURE" --diagnostics \
  --label single-file-workaround-fixture | tee "$EVIDENCE/single-file-workaround-fixture.txt"

echo "done; plain single-file load is expected to fail (see report.md)"
