#!/usr/bin/env bash

detect_rid() {
  local label="${1:-rid}" os arch

  case "$(uname -s)" in
    Darwin) os="osx" ;;
    Linux) os="linux" ;;
    MINGW*|MSYS*|CYGWIN*) os="win" ;;
    *) echo "${label}: unsupported OS: $(uname -s); set RID explicitly, for example RID=linux-x64" >&2; return 1 ;;
  esac

  case "$(uname -m)" in
    arm64|aarch64) arch="arm64" ;;
    x86_64|amd64) arch="x64" ;;
    *) echo "${label}: unsupported architecture: $(uname -m); set RID explicitly, for example RID=linux-x64" >&2; return 1 ;;
  esac

  echo "${os}-${arch}"
}

# Parses a shard spec "i/n" and prints "<i> <n>" (both integers, 1 <= i <= n).
# Exits 2 on anything else, including an out-of-range shard: an unreachable
# shard (i > n, or n = 0) must fail loudly rather than silently run nothing.
# The gate scripts source this and call it with the value of --shard.
shard_spec_parse() {
  local spec="${1:-}" i n
  i="${spec%%/*}"
  n="${spec#*/}"
  if [ "$i" = "$spec" ] || [ "$n" = "$spec" ]; then
    echo "shard spec must be <i>/<n>, got '$spec'" >&2
    return 2
  fi
  case "$i" in
    "" | *[!0-9]* | 0) echo "shard index must be an integer 1..n, got '$i'" >&2; return 2 ;;
  esac
  case "$n" in
    "" | *[!0-9]* | 0) echo "shard count must be a positive integer, got '$n'" >&2; return 2 ;;
  esac
  if [ "$i" -gt "$n" ]; then
    echo "shard index $i is out of range (1..$n)" >&2
    return 2
  fi
  printf '%s\n' "$i" "$n"
}

# Fails when any existing PublicAPI.Shipped.txt is edited or deleted relative
# to the merge base with main; a newly added tracking file is the bootstrap
# path for a project that starts tracking its surface and does not trip the gate.
# The comparison base is the merge base with origin/main, then main, then HEAD
# (working tree only) when neither exists; the chosen base is printed on breach.
check_shipped_api_unchanged() {
  local base=""

  base="$(git merge-base HEAD origin/main 2>/dev/null)" || base=""
  if [ -z "$base" ]; then
    base="$(git merge-base HEAD main 2>/dev/null)" || base=""
  fi
  if [ -z "$base" ]; then
    base="HEAD"
    echo "note: no main ref found; comparing the working tree only" >&2
  fi

  if ! git diff --exit-code --diff-filter=MD "$base" -- '*PublicAPI.Shipped.txt'; then
    echo "verify: PublicAPI.Shipped.txt changed relative to ${base}" >&2
    return 1
  fi
}
