#!/bin/sh
# Lunate installer for macOS (arm64) and Linux (x64).
#
# One-liner:
#   curl -fsSL https://raw.githubusercontent.com/wdaniel1993/lunate/main/install.sh | sh
#
# Usage as a file:
#   sh install.sh [--version <tag>] [--prefix <dir>]
#
# Environment overrides:
#   LUNATE_INSTALL_BASE_URL  releases base URL
#                            (default: https://github.com/wdaniel1993/lunate/releases)
#   LUNATE_INSTALL_VERSION   release tag to install, for example v0.1.0
#   LUNATE_INSTALL_PREFIX    install directory (default: $HOME/.local/bin)

set -eu

fail() {
  printf 'install: %s\n' "$1" >&2
  exit 1
}

usage() {
  cat <<'EOF'
Usage: sh install.sh [--version <tag>] [--prefix <dir>]

Installs the lunate release archive for this platform.

Options:
  --version <tag>  install a specific release tag, for example v0.1.0
  --prefix <dir>   install directory (default: $HOME/.local/bin)
  --help           show this help

Environment:
  LUNATE_INSTALL_BASE_URL  releases base URL; the script downloads from
                           <base>/latest/download or <base>/download/<tag>
  LUNATE_INSTALL_VERSION   release tag to install (same as --version)
  LUNATE_INSTALL_PREFIX    install directory (same as --prefix)
EOF
}

version="${LUNATE_INSTALL_VERSION:-}"
prefix="${LUNATE_INSTALL_PREFIX:-}"

while [ $# -gt 0 ]; do
  case "$1" in
    --version)
      [ $# -ge 2 ] || fail "--version requires a tag, for example: --version v0.1.0"
      version="$2"
      shift 2
      ;;
    --prefix)
      [ $# -ge 2 ] || fail "--prefix requires a directory"
      prefix="$2"
      shift 2
      ;;
    --help)
      usage
      exit 0
      ;;
    *)
      fail "unknown argument: $1 (see --help)"
      ;;
  esac
done

os="$(uname -s)"
arch="$(uname -m)"

case "$os" in
  Darwin)
    case "$arch" in
      arm64) rid="osx-arm64" ;;
      *)
        fail "unsupported platform: macOS on ${arch}; lunate publishes a macOS arm64 build. On Windows use install.ps1"
        ;;
    esac
    ;;
  Linux)
    case "$arch" in
      x86_64 | amd64) rid="linux-x64" ;;
      *)
        fail "unsupported platform: Linux on ${arch}; lunate publishes a Linux x86_64 build. On Windows use install.ps1"
        ;;
    esac
    ;;
  *)
    fail "unsupported platform: ${os} ${arch}; on Windows use install.ps1"
    ;;
esac

if [ -z "$prefix" ]; then
  [ -n "${HOME:-}" ] || fail "HOME is not set; pass --prefix <dir> or set LUNATE_INSTALL_PREFIX"
  prefix="${HOME}/.local/bin"
fi

command -v curl >/dev/null 2>&1 || fail "curl is required but was not found on PATH; install curl and re-run"

base="${LUNATE_INSTALL_BASE_URL:-https://github.com/wdaniel1993/lunate/releases}"
if [ -n "$version" ]; then
  case "$version" in
    v*) tag="$version" ;;
    *) tag="v${version}" ;;
  esac
  root="${base}/download/${tag}"
else
  root="${base}/latest/download"
fi

asset="lunate-${rid}.tar.gz"
tmp="$(mktemp -d "${TMPDIR:-/tmp}/lunate-install.XXXXXX")" || fail "cannot create a temporary directory"
cleanup() {
  rm -rf "$tmp"
}
trap cleanup EXIT

curl -fsSL -o "$tmp/$asset" "$root/$asset" || fail "failed to download ${root}/${asset}"
curl -fsSL -o "$tmp/SHA256SUMS" "$root/SHA256SUMS" || fail "failed to download ${root}/SHA256SUMS"

expected="$(awk -v name="$asset" '$2 == name { print $1; exit }' "$tmp/SHA256SUMS")"
[ -n "$expected" ] || fail "SHA256SUMS does not list ${asset}"

if command -v sha256sum >/dev/null 2>&1; then
  actual="$(sha256sum "$tmp/$asset" | awk '{ print $1 }')"
else
  command -v shasum >/dev/null 2>&1 || fail "neither sha256sum nor shasum is available; cannot verify the download"
  actual="$(shasum -a 256 "$tmp/$asset" | awk '{ print $1 }')"
fi

[ "$actual" = "$expected" ] || fail "checksum mismatch for ${asset}: expected ${expected}, got ${actual}; nothing was installed"

mkdir -p "$tmp/extract" || fail "cannot create the extraction directory"
tar -xzf "$tmp/$asset" -C "$tmp/extract" || fail "failed to extract ${asset}"
[ -f "$tmp/extract/lunate" ] || fail "${asset} does not contain a lunate binary"

mkdir -p "$prefix" || fail "cannot create the install directory: ${prefix}"
staged="${prefix}/.lunate-new.$$"
cp "$tmp/extract/lunate" "$staged" || fail "cannot copy lunate into ${prefix}"
chmod 0755 "$staged" || fail "cannot make lunate executable in ${prefix}"
mv -f "$staged" "$prefix/lunate" || fail "cannot install lunate into ${prefix}"

installed="${prefix}/lunate"
if version_output="$("$installed" --version 2>/dev/null)"; then
  printf 'installed lunate %s to %s\n' "$version_output" "$installed"
else
  printf 'installed lunate to %s\n' "$installed"
fi

case ":${PATH:-}:" in
  *":${prefix}:"*) ;;
  *)
    printf 'note: %s is not on your PATH; add it with: export PATH="%s:$PATH"\n' "$prefix" "$prefix"
    ;;
esac
