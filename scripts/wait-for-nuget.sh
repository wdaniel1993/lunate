#!/usr/bin/env bash
# Waits until nuget.org lists a package version, and serves its content, before an installer asks
# for it.
#
# A freshly pushed package is invisible until the feed indexes it: the flatcontainer index, the
# package content and the registration resource trail each other by minutes. An installer that
# retries a fixed number of times turns that lag into an opaque failure, so the release smoke waits
# on the feed itself — the three resources a package client reads, in the order it reads them — and
# reports which one is still missing when the budget runs out.
#
# Usage:
#   wait-for-nuget.sh --package <id> --version <version>
#                     [--budget-seconds 600] [--interval-seconds 10]
#                     [--flatcontainer-base <url>] [--registration-base <url>]
#
# Exit codes: 0 the feed lists the version and serves its content; 1 the budget ran out; 2 usage.
#
# The two bases are the test seam: a fixture feed served from whatever curl reads (file:// works)
# stands in for nuget.org.

set -euo pipefail

usage() {
  sed -n '2,22p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

package=""
version=""
budget_seconds=600
interval_seconds=10
flatcontainer_base="https://api.nuget.org/v3-flatcontainer"
registration_base="https://api.nuget.org/v3/registration5-gz-semver2"

while [ $# -gt 0 ]; do
  case "$1" in
    --package) package="${2:-}"; shift 2 ;;
    --version) version="${2:-}"; shift 2 ;;
    --budget-seconds) budget_seconds="${2:-}"; shift 2 ;;
    --interval-seconds) interval_seconds="${2:-}"; shift 2 ;;
    --flatcontainer-base) flatcontainer_base="${2:-}"; shift 2 ;;
    --registration-base) registration_base="${2:-}"; shift 2 ;;
    -h | --help) usage ;;
    *) echo "wait-for-nuget: unknown argument '$1'" >&2; usage ;;
  esac
done

[ -n "$package" ] && [ -n "$version" ] || usage
for value in "$budget_seconds" "$interval_seconds"; do
  case "$value" in
    '' | *[!0-9]*) echo "wait-for-nuget: '$value' is not a whole number of seconds" >&2; exit 2 ;;
  esac
done
[ "$budget_seconds" -ge 1 ] || { echo "wait-for-nuget: the budget must be at least one second" >&2; exit 2; }
[ "$interval_seconds" -ge 1 ] || { echo "wait-for-nuget: the interval must be at least one second" >&2; exit 2; }
for tool in curl jq; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "wait-for-nuget: $tool is required to read the feed" >&2
    exit 2
  fi
done

# NuGet lowercases both the id and the version in every URL it serves.
id="$(printf '%s' "$package" | tr '[:upper:]' '[:lower:]')"
lower_version="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"
index_url="${flatcontainer_base}/${id}/index.json"
content_url="${flatcontainer_base}/${id}/${lower_version}/${id}.${lower_version}.nupkg"
registration_url="${registration_base}/${id}/index.json"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# One request. FETCH_STATUS is the HTTP status, "000" when the transport served something without
# one (file://), or "failed" when curl could not complete the request at all.
FETCH_STATUS=""
FETCH_ERROR=""
fetch() {
  local url="$1" out="$2" status
  FETCH_STATUS=""
  FETCH_ERROR=""
  # --compressed: the registration resource is served gzipped (registration5-gz-semver2).
  if status="$(curl --silent --show-error --location --compressed --max-time 20 --retry 2 \
    --retry-delay 2 --write-out '%{http_code}' --output "$out" "$url" 2>"${out}.err")"; then
    FETCH_STATUS="${status:-000}"
  else
    FETCH_STATUS="failed"
    FETCH_ERROR="$(head -n 1 "${out}.err" 2>/dev/null || true)"
  fi
}

fetch_head() {
  local url="$1" status
  FETCH_STATUS=""
  FETCH_ERROR=""
  if status="$(curl --silent --show-error --location --head --max-time 20 --retry 2 --retry-delay 2 \
    --write-out '%{http_code}' --output "${work}/content.headers" "$url" 2>"${work}/content.err")"; then
    FETCH_STATUS="${status:-000}"
  else
    FETCH_STATUS="failed"
    FETCH_ERROR="$(head -n 1 "${work}/content.err" 2>/dev/null || true)"
  fi
}

served() {
  case "$FETCH_STATUS" in
    200 | 000) return 0 ;;
    *) return 1 ;;
  esac
}

# The feed's answer in words, for the report a human reads when the budget runs out.
fetched() {
  case "$FETCH_STATUS" in
    200) printf 'HTTP 200' ;;
    000) printf 'served without a status' ;;
    failed) printf 'unreachable%s' "${FETCH_ERROR:+ (${FETCH_ERROR})}" ;;
    *) printf 'HTTP %s' "$FETCH_STATUS" ;;
  esac
}

# Each gate keeps its last answer as one line a human can read.
index_ok=0 index_line=""
content_ok=0 content_line=""
registration_ok=0 registration_line=""

probe_index() {
  index_ok=0
  fetch "$index_url" "${work}/index.json"
  if ! served; then
    index_line="$(fetched); the feed did not serve the package's index"
    return 0
  fi
  local count newest
  if ! count="$(jq -r '.versions | if type == "array" then length else error("no versions") end' \
    "${work}/index.json" 2>/dev/null)"; then
    index_line="$(fetched); the response is not a flatcontainer index"
    return 0
  fi
  newest="$(jq -r '.versions[-1] // ""' "${work}/index.json" 2>/dev/null || true)"
  index_line="$(fetched); ${count} version(s)${newest:+, newest ${newest}}"
  if jq -e --arg v "$lower_version" \
    '.versions | map(ascii_downcase) | index($v) != null' "${work}/index.json" >/dev/null 2>&1; then
    index_ok=1
  else
    index_line="${index_line}; ${version} absent"
  fi
}

probe_content() {
  content_ok=0
  fetch_head "$content_url"
  if served; then
    content_ok=1
    content_line="$(fetched); the .nupkg is downloadable"
  else
    content_line="$(fetched); the .nupkg is not downloadable"
  fi
}

# Registration is two levels deep: the index lists pages, a page lists the versions. nuget.org
# inlines the leaves of the first page and links the rest, so both shapes are read here.
probe_registration() {
  registration_ok=0
  fetch "$registration_url" "${work}/registration.json"
  if ! served; then
    registration_line="$(fetched); the feed did not serve the package's registration"
    return 0
  fi
  local leaves="${work}/registration-versions.txt" pages=0 page count
  : >"$leaves"
  jq -r '.items[]? | select(.items) | .items[]? | (.catalogEntry.version // empty)' \
    "${work}/registration.json" 2>/dev/null >"$leaves" || true
  while IFS= read -r page; do
    [ -n "$page" ] || continue
    fetch "$page" "${work}/registration-page.json"
    served || continue
    pages=$((pages + 1))
    jq -r '.items[]? | (.catalogEntry.version // empty)' "${work}/registration-page.json" \
      2>/dev/null >>"$leaves" || true
  done <<EOF
$(jq -r '.items[]? | select(.items | not) | (.["@id"] // empty)' "${work}/registration.json" 2>/dev/null || true)
EOF
  count="$(grep -c . "$leaves" 2>/dev/null || true)"
  registration_line="$(fetched); ${count:-0} version(s) across ${pages} linked page(s)"
  if grep -qixF "$version" "$leaves"; then
    registration_ok=1
  else
    registration_line="${registration_line}; ${version} absent"
  fi
}

printf 'wait-for-nuget: waiting up to %ss for nuget.org to list %s %s\n' \
  "$budget_seconds" "$package" "$version"
printf 'wait-for-nuget:   index        %s\n' "$index_url"
printf 'wait-for-nuget:   content      %s\n' "$content_url"
printf 'wait-for-nuget:   registration %s\n' "$registration_url"

start=$SECONDS
index_at="" content_at="" registration_at=""
while :; do
  probe_index
  content_ok=0 registration_ok=0
  content_line="not checked (the index does not list the version)"
  registration_line="not checked (the index does not list the version)"
  if [ "$index_ok" -eq 1 ]; then
    probe_content
    if [ "$content_ok" -eq 1 ]; then
      probe_registration
    else
      registration_line="not checked (the .nupkg is not downloadable yet)"
    fi
  fi
  elapsed=$((SECONDS - start))
  [ "$index_ok" -eq 1 ] && [ -z "$index_at" ] && index_at="$elapsed"
  [ "$content_ok" -eq 1 ] && [ -z "$content_at" ] && content_at="$elapsed"
  [ "$registration_ok" -eq 1 ] && [ -z "$registration_at" ] && registration_at="$elapsed"

  if [ "$index_ok" -eq 1 ] && [ "$content_ok" -eq 1 ] && [ "$registration_ok" -eq 1 ]; then
    printf 'wait-for-nuget: nuget.org lists %s %s after %ss\n' "$package" "$version" "$elapsed"
    printf 'wait-for-nuget:   index        %s; listed after %ss\n' "$index_line" "$index_at"
    printf 'wait-for-nuget:   content      %s; listed after %ss\n' "$content_line" "$content_at"
    printf 'wait-for-nuget:   registration %s; listed after %ss\n' "$registration_line" "$registration_at"
    exit 0
  fi

  printf 'wait-for-nuget: %s not listed yet after %ss\n' "$version" "$elapsed"
  printf 'wait-for-nuget:   index        %s\n' "$index_line"
  printf 'wait-for-nuget:   content      %s\n' "$content_line"
  printf 'wait-for-nuget:   registration %s\n' "$registration_line"

  if [ "$elapsed" -ge "$budget_seconds" ]; then
    echo "wait-for-nuget: nuget.org did not list $package $version within ${budget_seconds}s; the feed is still not ready, so the install was not attempted" >&2
    echo "wait-for-nuget: check that the tag's Release run pushed the package (https://www.nuget.org/packages/$package), then re-run the smoke" >&2
    exit 1
  fi

  pause=$((budget_seconds - elapsed))
  [ "$pause" -gt "$interval_seconds" ] && pause="$interval_seconds"
  [ "$pause" -lt 1 ] && pause=1
  sleep "$pause"
done
