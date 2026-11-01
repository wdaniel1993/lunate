#!/usr/bin/env bash
# Packs every package the release workflow packs, and runs the same metadata check, without
# publishing anything. CI runs this on every pull request so a moved, renamed or deleted project
# fails on the PR that moves it instead of at tag time — which is how the supported extension moved
# from samples/extensions/subagents to extensions/subagents and nothing noticed until a release
# rehearsal.
#
# The list must mirror `.github/workflows/release.yml`'s pack steps. When a project joins or leaves
# the release, change both; the failure mode of forgetting is silent (a package simply is not
# built), which is why this file exists next to the workflow rather than inside a YAML step.
#
# usage: pack-dry-run.sh [output-dir]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
OUT="${1:-${RUNNER_TEMP:-/tmp}/lunate-pack-dry-run}"

# The output directory is deleted before packing. A caller-supplied path must therefore be a
# scratch directory and nothing else: refuse a filesystem root, the home directory, the repository
# or any ancestor of it, and refuse to clear a non-empty directory this script did not create.
resolved="$(cd "${OUT}" 2>/dev/null && pwd -P || printf '%s' "${OUT}")"
for guarded in "/" "${HOME}" "${REPO_ROOT}"; do
    if [ "${resolved}" = "${guarded}" ]; then
        echo "pack-dry-run: refusing to use ${resolved} as an output directory" >&2
        exit 2
    fi
done
case "${REPO_ROOT}/" in
    "${resolved}"/*)
        echo "pack-dry-run: refusing to use ${resolved}, which contains the repository" >&2
        exit 2
        ;;
esac
if [ -d "${resolved}" ] && [ -n "$(ls -A "${resolved}")" ] && [ ! -f "${resolved}/.pack-dry-run" ]; then
    echo "pack-dry-run: ${resolved} is not empty and was not created by this script" >&2
    exit 2
fi

cd "${REPO_ROOT}"
rm -rf "${resolved}"
mkdir -p "${resolved}"
: > "${resolved}/.pack-dry-run"

pack() {
    dotnet pack "$1" -c Release -o "${OUT}" --nologo
}

pack src/Lunate.Coding/Lunate.Coding.csproj
pack extensions/subagents/SubagentsExtension/SubagentsExtension.csproj
pack src/Lunate.Contracts/Lunate.Contracts.csproj
pack src/Lunate.Extensibility.Abstractions/Lunate.Extensibility.Abstractions.csproj
pack src/Lunate.Agent/Lunate.Agent.csproj
pack src/Lunate.Extensibility.Testing/Lunate.Extensibility.Testing.csproj

product_version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
# The versions the release rule derives from the tree's own version, read as a tag. Comparing the
# packed file names against this is the end-to-end half of the rule: a package that packs under a
# different version than the derivation says is a release-blocking mismatch, not a warning.
while IFS='=' read -r key value; do printf -v "derived_${key}" '%s' "${value}"; done \
    < <(bash scripts/release-versions.sh "v${product_version}")
contract_version="${derived_CONTRACTS}"
preview_version="${derived_PREVIEW}"

for expected in \
    "lunate.${derived_TOOL}.nupkg" \
    "Lunate.Extensions.Subagents.${derived_TOOL}.nupkg" \
    "Lunate.Contracts.${derived_CONTRACTS}.nupkg" \
    "Lunate.Extensibility.Abstractions.${derived_CONTRACTS}.nupkg" \
    "Lunate.Agent.${derived_PREVIEW}.nupkg" \
    "Lunate.Extensibility.Testing.${derived_PREVIEW}.nupkg"; do
    if [ ! -f "${OUT}/${expected}" ]; then
        echo "pack-dry-run: FAIL: expected ${expected} — the packed names disagree with the version rule for v${product_version}" >&2
        echo "pack-dry-run: packed: $(ls "${OUT}" | tr '\n' ' ')" >&2
        exit 1
    fi
done

bash scripts/check-package-metadata.sh "${OUT}/lunate.${product_version}.nupkg" --id lunate
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Extensions.Subagents.${product_version}.nupkg" --id Lunate.Extensions.Subagents
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Contracts.${contract_version}.nupkg" --id Lunate.Contracts
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Extensibility.Abstractions.${contract_version}.nupkg" --id Lunate.Extensibility.Abstractions
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Agent.${preview_version}.nupkg" --id Lunate.Agent --prerelease
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Extensibility.Testing.${preview_version}.nupkg" --id Lunate.Extensibility.Testing --prerelease

echo "pack-dry-run: $(ls "${OUT}" | wc -l | tr -d ' ') packages built and checked in ${OUT}"
