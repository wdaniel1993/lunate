#!/usr/bin/env bash
# Regenerate the README media (the demo GIF and the feature GIFs) from the committed
# VHS tapes. Keyless and deterministic: every tape replays a committed fixture through
# LUNATE_REPLAY with a pinned LUNATE_DATE, so no provider is called and no run varies.
# The demo tapes replay docs/media/fixtures/demo-session.jsonl; the thinking tape (see its
# fixture block below) replays tests/fixtures/streams/scripted-reasoning.jsonl.
#
# The fixture embeds the demo workspace path, the tool set and the model id in its request
# digests — it is only valid for exactly this setup. When the demo workspace, the prompt, the
# model id or the tool list changes, re-record it once (needs an OpenRouter key in
# ~/.lunate-demo-home/.lunate/auth.json):
#
#   LUNATE_RECORD=1 LUNATE_RECORD_PATH=<fixture> LUNATE_DATE=2026-10-18 \
#     HOME=/tmp/lunate-demo-home <lunate binary>   # then drive the same prompt and answers
#
# The recording and every render run must use the same environment: tool results are part of
# the request digests, so the locale, PATH, SDK first-run state and shell tools all have to
# match. The scripts in $SCRATCH/bin mask build timings and listing dates so replayed tool
# results stay byte-identical, and the scratch home is pre-warmed below.
#
# Requirements: vhs (brew install vhs), ffmpeg, and a prior Release build of the repo.
# Pass a tape-name fragment to render one tape only (for example "features-tools");
# without an argument every committed tape renders.
set -euo pipefail

FILTER="${1:-}"

# One coherent SDK install for the app's C# tools, the bash builds and the recording alike;
# a UTF-8 locale because ls collation is locale-sensitive and lands in the digests.
export DOTNET_ROOT="/usr/local/share/dotnet"
export LANG="en_US.UTF-8"
export TMPDIR="/tmp"
unset MSBuildSDKsPath MSBuildExtensionsPath MSBuildExtensionsPath32 MSBuildUserExtensionsPath 2>/dev/null || true

REPO="$(cd "$(dirname "$0")/.." && pwd)"
MEDIA="$REPO/docs/media"
SCRATCH=/tmp/lunate-media
DEMO=/tmp/lunate-demo
TRUST=/tmp/lunate-demo-trust
DEMO_HOME=/tmp/lunate-demo-home
BIN="$REPO/src/Lunate.Coding/bin/Release/net10.0/lunate"
PINNED_DATE=2026-10-18
# The request digest of the thinking tape (see the fixture block below): the digest of that
# tape's own request in this pinned environment, not the digest committed in the 6.1 fixture.
THINKING_DIGEST=25b80a3855b5ab46969063548319b68b9dd1c6ebcccf9d13ab245fa80a488597
GATE_EXT="$REPO/extensions/permission-gate/PermissionGateExtension"
VHS=/opt/homebrew/bin/vhs
# The tapes normalize their own PATH to this value before launching lunate (recording parity).
DEMO_PATH="/usr/local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin"

command -v ffmpeg >/dev/null || { echo "ffmpeg missing"; exit 1; }
[ -x "$VHS" ] || { echo "vhs missing: brew install vhs"; exit 1; }
[ -x "$BIN" ] || { echo "build first: dotnet build lunate.slnx -c Release"; exit 1; }
[ -f "$MEDIA/fixtures/demo-session.jsonl" ] || { echo "missing $MEDIA/fixtures/demo-session.jsonl"; exit 1; }

write_demo_workspace() {
  rm -rf "$DEMO"
  mkdir -p "$DEMO"
  cat > "$DEMO/demo.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF
  cat > "$DEMO/TodoList.cs" <<'EOF'
namespace Demo;

public sealed class TodoList
{
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items => _items;

    public void Add(string title)
    {
        _items.Add(title);
    }
}
EOF
  cat > "$DEMO/Program.cs" <<'EOF'
using Demo;

var list = new TodoList();
list.Add("write the demo");
Console.WriteLine($"{list.Items.Count} item(s)");
EOF
}

# --- scratch home: replay needs settings + catalog only (the seam skips credentials) ---
rm -rf "$DEMO_HOME" "$TRUST" "$SCRATCH"
mkdir -p "$DEMO_HOME/.lunate" "$SCRATCH/bin"
cat > "$DEMO_HOME/.lunate/settings.json" <<'JSON'
{ "schemaVersion": 1, "model": "deepseek/deepseek-v4.1-flash" }
JSON
cat > "$DEMO_HOME/.lunate/models.json" <<'JSON'
{
  "schemaVersion": 1,
  "models": [
    {
      "id": "deepseek/deepseek-v4.1-flash",
      "provider": "openai",
      "endpoint": "https://openrouter.ai/api/v1",
      "contextWindow": 1000000,
      "supportsTools": true,
      "auth": "openrouter"
    }
  ]
}
JSON

# --- pre-warm the SDK first-run state in the scratch home: the first dotnet use prints
# telemetry / certificate / workload notices that would otherwise vary between runs ---
(cd /tmp && HOME="$DEMO_HOME" "$DOTNET_ROOT/dotnet" --info >/dev/null 2>&1 || true)
(cd /tmp && HOME="$DEMO_HOME" "$DOTNET_ROOT/dotnet" workload list >/dev/null 2>&1 || true)
(cd /tmp && HOME="$DEMO_HOME" "$DOTNET_ROOT/dotnet" dev-certs https >/dev/null 2>&1 || true)

# --- the demo workspace: fixed file contents (the fixture's digests depend on them) ---
write_demo_workspace

# --- the trust workspace: a copy of the demo plus a project extension (the trust prompt) ---
mkdir -p "$TRUST/.lunate/extensions/permission-gate"
cp "$DEMO/demo.csproj" "$DEMO/TodoList.cs" "$DEMO/Program.cs" "$TRUST/"
# The repo pins its SDK via global.json; build with the user install explicitly, not the
# demo-coherent DOTNET_ROOT exported above.
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" build "$GATE_EXT/PermissionGateExtension.csproj" -c Release --nologo >/dev/null
cp "$GATE_EXT"/bin/Release/net10.0/*.dll "$TRUST/.lunate/extensions/permission-gate/"
cp "$GATE_EXT/extension.json" "$TRUST/.lunate/extensions/permission-gate/"

# --- the thinking tape's fixture: the committed 6.1 scripted reasoning fixture with the digest of
# the tape's own request. A replay checks the digest of the whole request, and the committed
# fixture carries the composition test's request shape (its short system prompt and four tools);
# a real session composes the system prompt from the workspace and offers the workspace's tools.
# Only the digest is replaced, so the tape stays driven by tests/fixtures/streams/scripted-reasoning.jsonl.
# When the workspace, the tool list, the prompt template, the date or the OS changes, the tape
# fails with a request mismatch: re-record the exchange with LUNATE_RECORD=1 (needs a provider key)
# in this environment and update THINKING_DIGEST from the recorded fixture. ---
sed -E "s/\"requestDigest\":\"[0-9a-f]{64}\"/\"requestDigest\":\"$THINKING_DIGEST\"/" \
  "$REPO/tests/fixtures/streams/scripted-reasoning.jsonl" > "$SCRATCH/thinking-session.jsonl"

# --- the wrappers the tapes invoke: replay + pinned date + the scratch home ---
write_replay_wrapper() { # directory, replay fixture
  mkdir -p "$1"
  cat > "$1/lunate" <<EOF
#!/bin/bash
export HOME="$DEMO_HOME"
export LUNATE_REPLAY="$2"
export LUNATE_DATE="$PINNED_DATE"
exec "$BIN" "\$@"
EOF
  chmod +x "$1/lunate"
}
cp "$MEDIA/fixtures/demo-session.jsonl" "$SCRATCH/demo-session.jsonl"
write_replay_wrapper "$SCRATCH/bin" "$SCRATCH/demo-session.jsonl"
write_replay_wrapper "$SCRATCH/bin-thinking" "$SCRATCH/thinking-session.jsonl"

# --- deterministic-output shims (see the header): mask build timings and listing dates ---
cat > "$SCRATCH/bin/dotnet" <<'SHIM'
#!/bin/bash
real=/usr/local/share/dotnet/dotnet
set -o pipefail
"$real" "$@" 2>&1 | sed -E '/^Time Elapsed /d; s/\(in [0-9.]+ ?ms\)/(in 1 ms)/g'
exit ${PIPESTATUS[0]}
SHIM
chmod +x "$SCRATCH/bin/dotnet"
cat > "$SCRATCH/bin/ls" <<'SHIM'
#!/bin/bash
/bin/ls "$@" 2>&1 | sed -E \
  -e 's/[A-Z][a-z]{2} [ 0-9][0-9] [0-9]{2}:[0-9]{2}/Jan  1 00:00/g' \
  -e '/\.\.$/d'
exit ${PIPESTATUS[0]}
SHIM
chmod +x "$SCRATCH/bin/ls"

# --- render every tape from docs/media (relative paths resolve there); each demo tape gets
# the pristine workspace back, each trust tape an untrusted home ---
cd "$MEDIA"
for tape in tapes/${FILTER}*.tape; do
  echo "==> $tape"
  case "$tape" in
    *features-trust.tape|*features-extensions.tape)
      rm -f "$DEMO_HOME/.lunate/trust.json"
      ;;
    *)
      write_demo_workspace
      ;;
  esac
  "$VHS" "$tape"
done

# --- size guard: README GIFs stay under 5 MB ---
status=0
for gif in demo.gif features/*.gif; do
  bytes=$(stat -f%z "$gif" 2>/dev/null || stat -c%s "$gif")
  printf '%-28s %6.2f MB\n' "$gif" "$(echo "scale=2; $bytes / 1048576" | bc)"
  if [ "$bytes" -gt 5242880 ]; then echo "TOO BIG: $gif"; status=1; fi
done
echo "media render complete"
exit $status
