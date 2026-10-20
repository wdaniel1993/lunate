#!/usr/bin/env bash
# version: 1.0.0
# Regenerates the embedded catalog through ModelsDevCatalog; never maps in shell.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

SNAPSHOT=""
OUTPUT="${ROOT}/src/Lunate.Ai/models.json"
CHECK=0

usage() {
    cat <<'EOF'
usage: scripts/update-models.sh [--snapshot <path>] [--output <path>] [--check]

Regenerates the embedded model catalog through the C# ModelsDevCatalog mapper.
Without --snapshot, downloads ${MODELS_DEV_URL:-https://models.dev/api.json}.
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

if [ -z "${SNAPSHOT}" ]; then
    if ! command -v curl >/dev/null 2>&1; then
        echo "update-models: curl is required to fetch the models.dev snapshot" >&2
        exit 2
    fi
    TEMP="$(mktemp "${TMPDIR:-/tmp}/lunate-models-dev.XXXXXX")"
    URL="${MODELS_DEV_URL:-https://models.dev/api.json}"
    echo "fetching ${URL}"
    curl --fail --silent --show-error --location "${URL}" --output "${TEMP}"
    SNAPSHOT="${TEMP}"
fi

cd "${ROOT}"
ARGS=(run "${ROOT}/scripts/update-models.cs" -- --snapshot "${SNAPSHOT}" --output "${OUTPUT}")
if [ "${CHECK}" -eq 1 ]; then
    ARGS+=(--check)
fi
dotnet "${ARGS[@]}"
