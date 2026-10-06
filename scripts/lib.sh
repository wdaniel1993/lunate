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
