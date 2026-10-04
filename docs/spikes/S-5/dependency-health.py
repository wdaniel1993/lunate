#!/usr/bin/env python3
"""S-5 dependency health: pinned versions, licences, release cadence.

Queries the public NuGet registration API (no keys). Run:
    python3 docs/spikes/S-5/dependency-health.py > docs/spikes/S-5/evidence/dependency-health.txt
"""
import json
import sys
import urllib.request
from datetime import datetime, timezone

PINNED = {
    "Spectre.Console": "0.57.2",
    "System.Reactive": "7.0.0",
    "Microsoft.Reactive.Testing": "7.0.0",
    "ReactiveUI": "26.0.1",
    "Splat": "21.0.0",
    "Microsoft.Extensions.TimeProvider.Testing": "10.4.0",
    "xunit.v3": "4.0.1",
    "ReactiveUI.Core": "26.0.1",
    "ReactiveUI.Binding": "9.1.0",
    "ReactiveUI.Primitives": "9.0.0",
}

REG = "https://api.nuget.org/v3/registration5-semver1/{id_lower}/{version}.json"
INDEX = "https://api.nuget.org/v3/registration5-semver1/{id_lower}/index.json"


def get(url: str):
    request = urllib.request.Request(url, headers={"Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def leaf(package: str, version: str) -> dict:
    data = get(REG.format(id_lower=package.lower(), version=version))
    entry = data.get("catalogEntry", {})
    if isinstance(entry, str):
        entry = get(entry)
    return entry


def all_entries(package: str) -> list[dict]:
    index = get(INDEX.format(id_lower=package.lower()))
    entries = []
    for item in index.get("items", []):
        if "items" in item:
            entries.extend(entry["catalogEntry"] for entry in item["items"])
        else:
            page = get(item["@id"])
            entries.extend(entry["catalogEntry"] for entry in page.get("items", []))
    return entries


def stable(entries: list[dict]) -> list[dict]:
    return [e for e in entries if "-" not in e.get("version", "")]


def main() -> int:
    print("== pinned versions ==")
    for package, version in PINNED.items():
        catalog = leaf(package, version)
        published = catalog.get("published", "?")[:10]
        license_expr = catalog.get("licenseExpression") or catalog.get("licenseUrl") or "?"
        print(f"{package} {version}: published={published} license={license_expr}")

    print()
    print("== release cadence (last stable releases) ==")
    for package in PINNED:
        entries = stable(all_entries(package))
        entries.sort(key=lambda e: e.get("published", ""))
        recent = entries[-8:]
        dates = [(e["version"], e.get("published", "?")[:10]) for e in recent]
        if len(recent) >= 2:
            first = datetime.fromisoformat(recent[0]["published"].replace("Z", "+00:00"))
            last = datetime.fromisoformat(recent[-1]["published"].replace("Z", "+00:00"))
            span = (last - first).days
            per_release = span / (len(recent) - 1) if span else 0
            cadence = f"~{per_release:.1f} days between releases over the last {len(recent)}"
        else:
            cadence = "n/a"
        print(f"{package}: {cadence}")
        print("  " + ", ".join(f"{v} ({d})" for v, d in dates))

    print()
    print("== notes ==")
    print("ReactiveUI 26.x does not depend on System.Reactive; it ships its own")
    print("ReactiveUI.Primitives (RxVoid, ISignals, ISequencer) and Splat 21.")
    print("Mixing it with System.Reactive requires explicit interop at IObservable seams")
    print("(and Subscribe() extension calls are ambiguous - see S5.VariantBPlus).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
