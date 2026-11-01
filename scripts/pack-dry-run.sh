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

cd "${REPO_ROOT}"
rm -rf "${OUT}"
mkdir -p "${OUT}"

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
# The contract csprojs carry `$(ContractVersion)`; read the computed value from the packed file so
# the check never depends on how MSBuild expanded the property (it follows the product's prerelease
# suffix: 0.2.0-rc.1 -> 1.15.0-rc.1).
contract_version="$(ls "${OUT}" | sed -n 's/^Lunate\.Contracts\.\(.*\)\.nupkg$/\1/p' | head -n 1)"
preview_version="$(ls "${OUT}" | sed -n 's/^Lunate\.Agent\.\(.*\)\.nupkg$/\1/p' | head -n 1)"

bash scripts/check-package-metadata.sh "${OUT}/lunate.${product_version}.nupkg" --id lunate
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Extensions.Subagents.${product_version}.nupkg" --id Lunate.Extensions.Subagents
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Contracts.${contract_version}.nupkg" --id Lunate.Contracts
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Extensibility.Abstractions.${contract_version}.nupkg" --id Lunate.Extensibility.Abstractions
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Agent.${preview_version}.nupkg" --id Lunate.Agent --prerelease
bash scripts/check-package-metadata.sh "${OUT}/Lunate.Extensibility.Testing.${preview_version}.nupkg" --id Lunate.Extensibility.Testing --prerelease

echo "pack-dry-run: $(ls "${OUT}" | wc -l | tr -d ' ') packages built and checked in ${OUT}"
