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
