#!/usr/bin/env bash
# S-5 crash probe: publish a variant with the exact release flags (single file +
# ReadyToRun + compression) and without compression, run each N times, count
# fatal starts. ADR-0008 follow-up; results feed docs/spikes/S-5/.
# Usage: bash docs/spikes/S-5/crash-probe.sh <rid> [runs] [outdir]
set -euo pipefail

RID="${1:?usage: crash-probe.sh <rid> [runs] [outdir]}"
RUNS="${2:-100}"
OUT="${3:-artifacts/s5-crash-probe}"
S5=docs/spikes/S-5

export MSBUILDDISABLENODEREUSE=1
mkdir -p "$OUT"

probe() {
  local variant="$1" mode="$2"; shift 2
  local dir="$OUT/$variant-$mode"
  dotnet publish "$S5/S5.$variant/S5.$variant.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:PublishReadyToRun=true "$@" \
    -o "$dir" --nologo -v q
  local bin="$dir/S5.$variant"
  if [ "$RID" != "${RID#win}" ]; then
    bin="$bin.exe"
  fi
  local fails=0
  for i in $(seq 1 "$RUNS"); do
    if ! "$bin" --startup >/dev/null 2>>"$OUT/$variant-$mode-stderr.txt"; then
      fails=$((fails + 1))
    fi
  done
  echo "$variant $mode: runs=$RUNS non_zero_exit=$fails" | tee -a "$OUT/summary.txt"
}

for variant in Baseline VariantA VariantB VariantBPlus; do
  probe "$variant" compressed -p:EnableCompressionInSingleFile=true
  probe "$variant" uncompressed
done
echo "done: $OUT"
