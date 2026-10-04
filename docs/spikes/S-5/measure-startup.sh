#!/usr/bin/env bash
# S-5: startup delta and idle memory vs baseline (S-3 publish method: self-contained,
# single-file, ReadyToRun, compressed), plus added assemblies.
#
# B+ is unstable with the exact feature combination (single-file + ReadyToRun +
# compression): ~18% of startups die with AccessViolationException. It is
# therefore additionally published with compression off; that build is used for
# the startup/memory numbers and the crash rate of the exact-flags build is
# recorded as a finding.
#
# Usage: bash docs/spikes/S-5/measure-startup.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"
S5=docs/spikes/S-5
OUT=artifacts/s5
EVIDENCE=$S5/evidence
export MSBUILDDISABLENODEREUSE=1

case "$(uname -m)" in
  arm64) RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "unsupported arch" >&2; exit 1 ;;
esac

mkdir -p "$OUT" "$EVIDENCE"
variants=(Baseline VariantA VariantB)

publish() {
  local variant="$1" target="$2"; shift 2
  dotnet publish "$S5/S5.$variant/S5.$variant.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:PublishReadyToRun=true \
    -p:EnableCompressionInSingleFile=true "$@" \
    -o "$OUT/$target" --nologo -v q
}

echo "== publish single-file ReadyToRun ($RID, compression on) =="
for variant in "${variants[@]}" VariantBPlus; do
  publish "$variant" "$variant"
done
echo "== publish stable set (compression off; comparable packaging for all variants) =="
for variant in "${variants[@]}"; do
  publish "$variant" "$variant-stable" -p:EnableCompressionInSingleFile=false
done
publish VariantBPlus VariantBPlus-stable -p:EnableCompressionInSingleFile=false

echo "== startup crash probe (100 runs per variant, exact flags) =="
: > "$EVIDENCE/startup-crashes.txt"
for variant in "${variants[@]}" VariantBPlus; do
  fails=0
  crashes=0
  for _ in $(seq 1 100); do
    out=$("$OUT/$variant/S5.$variant" --startup 2>&1) || fails=$((fails + 1))
    case "$out" in *"Fatal error"*) crashes=$((crashes + 1)) ;; esac
  done
  echo "$variant: non_zero_exit=$fails fatal_crashes=$crashes" | tee -a "$EVIDENCE/startup-crashes.txt"
done

echo "== whole-process startup (hyperfine, 20 runs, 3 warmups) =="
: > "$EVIDENCE/startup-selfreport.txt"
for variant in "${variants[@]}"; do
  hyperfine --warmup 3 --runs 20 --export-json "$EVIDENCE/startup-$variant.json" \
    "$OUT/$variant/S5.$variant --startup" >/dev/null
  "$OUT/$variant/S5.$variant" --startup | tee -a "$EVIDENCE/startup-selfreport.txt"
done
echo "== whole-process startup, stable set (no compression) =="
: > "$EVIDENCE/startup-selfreport-medians.txt"
for variant in "${variants[@]}" VariantBPlus; do
  hyperfine --warmup 3 --runs 20 --export-json "$EVIDENCE/startup-$variant-stable.json" \
    "$OUT/$variant-stable/S5.$variant --startup" >/dev/null
  samples=""
  for _ in $(seq 1 20); do
    samples="$samples $("$OUT/$variant-stable/S5.$variant" --startup | sed -E 's/startup_ms=([0-9.]+).*/\1/')"
  done
  median=$(printf '%s\n' $samples | sort -n | awk '{a[NR]=$1} END {print a[int((NR+1)/2)]}')
  echo "$variant self_reported_startup_ms_median=$median (samples: $samples)" \
    | tee -a "$EVIDENCE/startup-selfreport-medians.txt"
done

echo "== idle memory (peak RSS, 150 ms sampling) =="
: > "$EVIDENCE/idle.txt"
for variant in "${variants[@]}"; do
  bash "$S5/mem-sample.sh" "$variant" "$EVIDENCE/idle-$variant.txt" \
    -- "$OUT/$variant/S5.$variant" --idle | tee -a "$EVIDENCE/idle.txt"
done
bash "$S5/mem-sample.sh" "VariantBPlus-stable" "$EVIDENCE/idle-VariantBPlus.txt" \
  -- "$OUT/VariantBPlus-stable/S5.VariantBPlus" --idle | tee -a "$EVIDENCE/idle.txt"

echo "== added assemblies (framework-dependent publish, diff vs baseline) =="
for variant in "${variants[@]}" VariantBPlus; do
  dotnet publish "$S5/S5.$variant/S5.$variant.csproj" \
    -c Release -r "$RID" --self-contained true \
    -o "$OUT/$variant-assemblies" --nologo -v q
  (cd "$OUT/$variant-assemblies" && ls ./*.dll | sed 's#^\./##' | sort) \
    > "$EVIDENCE/assemblies-$variant.txt"
done

: > "$EVIDENCE/assemblies-added.txt"
for variant in VariantA VariantB VariantBPlus; do
  echo "== $variant (vs Baseline)" >> "$EVIDENCE/assemblies-added.txt"
  comm -13 "$EVIDENCE/assemblies-Baseline.txt" "$EVIDENCE/assemblies-$variant.txt" \
    >> "$EVIDENCE/assemblies-added.txt"
done
wc -l "$EVIDENCE"/assemblies-*.txt | tee "$EVIDENCE/assembly-counts.txt"

echo "measure-startup: done"
