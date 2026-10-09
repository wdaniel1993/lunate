#!/usr/bin/env bash
# S-6: startup delta vs the S-5 baseline (same method as S-5: self-contained,
# single-file, ReadyToRun; whole-process hyperfine 3 warmups x 20 runs; the
# stable table uses compression off), idle memory via the S-5 sampler, added
# assemblies and the framework-dependent dependency closure, and a 100-start
# crash probe with the exact verify flags.
#
# The reference build is docs/spikes/S-5/S5.Baseline (read-only); its build
# output and artifacts/s6 are untracked.
# Usage: bash docs/spikes/S-6/scripts/measure-startup.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"
S5=docs/spikes/S-5
S6=docs/spikes/S-6
OUT=artifacts/s6
EVIDENCE=$S6/evidence
export MSBUILDDISABLENODEREUSE=1

case "$(uname -m)" in
  arm64) RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "unsupported arch" >&2; exit 1 ;;
esac

mkdir -p "$OUT" "$EVIDENCE"

publish() { # project outdir compression
  dotnet publish "$1" \
    -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:PublishReadyToRun=true \
    -p:EnableCompressionInSingleFile="$3" \
    -o "$2" --nologo -v q
}

publish_fd() { # framework-dependent, no single file, no R2R
  dotnet publish "$1" -c Release -r "$RID" --self-contained false \
    -o "$2" --nologo -v q
}

echo "== publish stable set (no compression; comparable packaging) =="
publish "$S5/S5.Baseline/S5.Baseline.csproj" "$OUT/baseline-stable" false
publish "$S6/S6.Harness/S6.Harness.csproj" "$OUT/xenoatom-stable" false

echo "== whole-process startup (hyperfine, 3 warmups, 20 runs) =="
for pair in "S5Baseline:$OUT/baseline-stable/S5.Baseline" "XenoAtom:$OUT/xenoatom-stable/S6.Harness"; do
  label="${pair%%:*}"; bin="${pair#*:}"
  hyperfine --warmup 3 --runs 20 --export-json "$EVIDENCE/startup-$label-stable.json" \
    "$bin --startup" >/dev/null
  samples=""
  for _ in $(seq 1 20); do
    samples="$samples $("$bin" --startup | sed -E 's/.*startup_ms=([0-9.]+).*/\1/')"
  done
  median=$(printf '%s\n' $samples | sort -n | awk '{a[NR]=$1} END {print a[int((NR+1)/2)]}')
  echo "$label self_reported_startup_ms_median=$median" | tee -a "$EVIDENCE/startup-selfreport.txt"
done

echo "== exact verify flags (compressed) + 100-start crash probe =="
publish "$S5/S5.Baseline/S5.Baseline.csproj" "$OUT/baseline-exact" true
publish "$S6/S6.Harness/S6.Harness.csproj" "$OUT/xenoatom-exact" true
: > "$EVIDENCE/startup-crashes.txt"
for pair in "S5Baseline:$OUT/baseline-exact/S5.Baseline" "XenoAtom:$OUT/xenoatom-exact/S6.Harness"; do
  label="${pair%%:*}"; bin="${pair#*:}"
  fails=0
  crashes=0
  for _ in $(seq 1 100); do
    out=$("$bin" --startup 2>&1) || fails=$((fails + 1))
    case "$out" in *"Fatal error"*) crashes=$((crashes + 1)) ;; esac
  done
  echo "$label: non_zero_exit=$fails fatal_crashes=$crashes" | tee -a "$EVIDENCE/startup-crashes.txt"
done

echo "== idle memory (peak RSS, 150 ms sampling; S-5 mem-sample.sh) =="
: > "$EVIDENCE/idle.txt"
bash "$S5/mem-sample.sh" "S5Baseline" "$EVIDENCE/idle-S5Baseline.txt" \
  -- "$OUT/baseline-stable/S5.Baseline" --idle | tee -a "$EVIDENCE/idle.txt"
bash "$S5/mem-sample.sh" "XenoAtom" "$EVIDENCE/idle-XenoAtom.txt" \
  -- "$OUT/xenoatom-stable/S6.Harness" --idle | tee -a "$EVIDENCE/idle.txt"

echo "== added assemblies (framework-dependent publish, diff vs baseline) =="
publish_fd "$S5/S5.Baseline/S5.Baseline.csproj" "$OUT/baseline-assemblies"
publish_fd "$S6/S6.Harness/S6.Harness.csproj" "$OUT/xenoatom-assemblies"
(cd "$OUT/baseline-assemblies" && ls ./*.dll | sed 's#^\./##' | sort) > "$EVIDENCE/assemblies-S5Baseline.txt"
(cd "$OUT/xenoatom-assemblies" && ls ./*.dll | sed 's#^\./##' | sort) > "$EVIDENCE/assemblies-XenoAtom.txt"
{
  echo "== XenoAtom (vs S5 baseline)"
  comm -13 "$EVIDENCE/assemblies-S5Baseline.txt" "$EVIDENCE/assemblies-XenoAtom.txt"
} > "$EVIDENCE/assemblies-added.txt"
wc -l "$EVIDENCE"/assemblies-*.txt | tee "$EVIDENCE/assembly-counts.txt"

echo "== dependency closure (transitive package graph) =="
dotnet list "$S6/S6.Harness/S6.Harness.csproj" package --include-transitive \
  > "$EVIDENCE/dependency-closure.txt" 2>&1
cat "$EVIDENCE/dependency-closure.txt"

echo "measure-startup: done"
