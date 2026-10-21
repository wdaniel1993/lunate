#!/usr/bin/env bash
# version: 1.0.0
# Regenerates the embedded catalog through the CLI's --update-catalog; never maps in shell.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${ROOT}/src/Lunate.Coding/Lunate.Coding.csproj"

SNAPSHOT=""
OUTPUT="${ROOT}/src/Lunate.Ai/models.json"
CHECK=0

usage() {
    cat <<'EOF'
usage: scripts/update-models.sh [--snapshot <path>] [--output <path>] [--check]

Regenerates the embedded model catalog through `lunate --update-catalog`, the one
ModelsDevCatalog entry point. Without --snapshot, the CLI fetches
${MODELS_DEV_URL:-https://models.dev/api.json}. With --check, generates to a temporary
file and compares it byte for byte with the embedded catalog (or --output).
EOF
}

resolve_path() {
    case "$1" in
        /*) printf '%s\n' "$1" ;;
        *) printf '%s/%s\n' "${PWD}" "$1" ;;
    esac
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --snapshot)
            if [ "$#" -lt 2 ]; then
                usage >&2
                exit 2
            fi
            SNAPSHOT="$(resolve_path "$2")"
            shift 2
            ;;
        --output)
            if [ "$#" -lt 2 ]; then
                usage >&2
                exit 2
            fi
            OUTPUT="$(resolve_path "$2")"
            shift 2
            ;;
        --check)
            CHECK=1
            shift
            ;;
        -h | --help)
            usage
            exit 0
            ;;
        *)
            echo "update-models: unknown argument '$1'" >&2
            usage >&2
            exit 2
            ;;
    esac
done

TEMP=""
cleanup() {
    if [ -n "${TEMP}" ]; then
        rm -f "${TEMP}"
    fi
}
trap cleanup EXIT

update_catalog() {
    local target="$1"
    if [ -n "${SNAPSHOT}" ]; then
        dotnet run --project "${PROJECT}" -- --update-catalog --output "${target}" --source "${SNAPSHOT}"
    elif [ -n "${MODELS_DEV_URL:-}" ]; then
        dotnet run --project "${PROJECT}" -- --update-catalog --output "${target}" --source "${MODELS_DEV_URL}"
    else
        dotnet run --project "${PROJECT}" -- --update-catalog --output "${target}"
    fi
}

if [ "${CHECK}" -eq 1 ]; then
    TEMP="$(mktemp "${TMPDIR:-/tmp}/lunate-models-check.XXXXXX")"
    if ! update_catalog "${TEMP}" >/dev/null; then
        echo "update-models: catalog generation failed; ${OUTPUT} was not changed" >&2
        exit 1
    fi
    if cmp -s "${TEMP}" "${OUTPUT}"; then
        echo "${OUTPUT} is up to date."
    else
        echo "error: ${OUTPUT} is out of date; run scripts/update-models.sh to regenerate it." >&2
        exit 1
    fi
else
    update_catalog "${OUTPUT}"
fi
