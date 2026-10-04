#!/usr/bin/env bash
# S-5 memory decomposition (revision item 3 + ADR-0009): what is the ~82 MB
# idle RSS made of (runtime, single-file packaging, R2R, Spectre, GC), and
# which GC settings change it? Publishes minimal probes and measures peak RSS
# of the process tree (mem-sample.sh, 150 ms sampling).
# Usage: bash docs/spikes/S-5/mem-probe.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"
S5=docs/spikes/S-5
OUT=artifacts/s5-mem
EVIDENCE=$S5/evidence
export MSBUILDDISABLENODEREUSE=1
mkdir -p "$OUT" "$EVIDENCE"

case "$(uname -m)" in
  arm64) RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "unsupported arch" >&2; exit 1 ;;
esac

publish() { # csproj outdir  (single file + R2R, uncompressed per ADR-0008)
  dotnet publish "$1" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:PublishReadyToRun=true \
    -o "$2" --nologo -v q
}

publish_fd() { # framework-dependent, no single file, no R2R
  dotnet publish "$1" -c Release -r "$RID" --self-contained false \
    -o "$2" --nologo -v q
}

publish_sf() { # single file, no R2R
  dotnet publish "$1" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:PublishReadyToRun=false \
    -o "$2" --nologo -v q
}

echo "== publish minimal probes =="
publish "$S5/mem-probe/hello/hello.csproj" "$OUT/hello"
publish "$S5/mem-probe/hello-spectre/hello-spectre.csproj" "$OUT/hello-spectre"
publish_fd "$S5/mem-probe/hello/hello.csproj" "$OUT/hello-fd"
publish_sf "$S5/mem-probe/hello/hello.csproj" "$OUT/hello-sf"

S5BASE=artifacts/s5/Baseline-stable/S5.Baseline
if [ ! -f "$S5BASE" ]; then
  publish "$S5/S5.Baseline/S5.Baseline.csproj" "$OUT/Baseline"
  S5BASE="$OUT/Baseline/S5.Baseline"
fi

RESULTS="$EVIDENCE/mem-decomposition.txt"
: > "$RESULTS"

run3() {
  local label="$1"; shift
  for i in 1 2 3; do
    bash "$S5/mem-sample.sh" "$label-$i" "$OUT/$label-$i.log" -- "$@" | tee -a "$RESULTS" || true
  done
}

echo "== floor split: framework-dependent (runtime only) =="
run3 hello-fd dotnet "$OUT/hello-fd/hello.dll"
echo "== floor split: single-file without R2R =="
run3 hello-sf "$OUT/hello-sf/hello"
echo "== floor: hello (runtime + single-file + R2R) =="
run3 hello "$OUT/hello/hello"
echo "== floor + Spectre =="
run3 hello-spectre "$OUT/hello-spectre/hello-spectre"
echo "== S5.Baseline --idle: default =="
run3 baseline-default "$S5BASE" --idle
echo "== S5.Baseline --idle: DOTNET_GCConserveMemory=9 =="
run3 baseline-conserve9 env DOTNET_GCConserveMemory=9 "$S5BASE" --idle
echo "== S5.Baseline --idle: DOTNET_gcServer=1 (contrast) =="
run3 baseline-servergc env DOTNET_gcServer=1 "$S5BASE" --idle
echo "== S5.Baseline --idle: DOTNET_TieredCompilation=0 =="
run3 baseline-tieredoff env DOTNET_TieredCompilation=0 "$S5BASE" --idle

echo "results: $RESULTS"
