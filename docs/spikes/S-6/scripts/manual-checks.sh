#!/usr/bin/env bash
# S-6: build and publish the interactive harness for the maintainer's terminal
# matrix, then print the exact procedure per terminal. Records nothing itself;
# capture each run into docs/spikes/S-6/evidence/manual-<terminal>.txt.
# Usage: bash docs/spikes/S-6/scripts/manual-checks.sh [RID]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"
S6=docs/spikes/S-6
OUT=artifacts/s6-manual
RID="${1:-}"

if [ -z "$RID" ]; then
  case "$(uname -m)" in
    arm64) RID=osx-arm64 ;;
    x86_64) RID=osx-x64 ;;
    *) RID=win-x64 ;;
  esac
fi

dotnet publish "$S6/S6.Harness/S6.Harness.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:PublishReadyToRun=true \
  -o "$OUT/$RID" --nologo -v q

cat <<EOF
published: $OUT/$RID ($RID)

Procedure (record OS, terminal, TERM, and the full screen into
docs/spikes/S-6/evidence/manual-<terminal>.txt):

1. Run:  $OUT/$RID/S6.Harness --interactive
2. Type "hello" -> Enter. Expect the line to appear as queued steering in the
   live area and the editor to clear.
3. Wait for the approval prompt (approve bash: dotnet test?).
   Press n, then a on the second prompt. Expect denied/auto-approved status.
4. Press Esc while the tool spinner runs. Expect "cancelling (Esc)" and the
   spinner to stop; the run must not quit.
5. Press Ctrl+C: input clears, app stays. Press Ctrl+C again within 2 s: app
   exits and the live region is kept (final frame).
6. Resize the window while streaming. Expect the live area to reflow, no
   leftover wrapped lines below it, and finished blocks to stay above it.
7. Note ANSI quality: colors, cursor visibility, mouse selection unaffected
   (inline mode does not enable mouse reporting).

Windows Terminal / conhost: run S6.Harness.exe from PowerShell/cmd (conhost:
run the same exe from a plain console window).
Git Bash (mintty): run S6.Harness.exe directly; per ADR-0004 the MSYS2 pseudo
console is required. Also try MSYS=disable_pcon to expose the fail-soft path.
macOS Terminal.app: ./S6.Harness --interactive
tmux: tmux new -s s6 '$OUT/$RID/S6.Harness --interactive'
  then Ctrl+B " to split while it runs; check the live region in both panes.
EOF
