#!/usr/bin/env python3
"""Classify [BREAKING] lines in Microsoft Agent Framework dotnet-* releases:
all MAF .NET packages vs harness-scoped (strict keyword match)."""
import json, re, subprocess, sys

rows = []
for page in (1, 2):
    raw = subprocess.run(
        ["gh", "api", f"repos/microsoft/agent-framework/releases?per_page=100&page={page}"],
        capture_output=True, text=True, check=True).stdout
    batch = json.loads(raw)
    if not batch:
        break
    rows.extend(batch)

rels = [r for r in rows if r["tag_name"].startswith("dotnet-")]
rels.sort(key=lambda r: r["published_at"])

print("| Tag | Published | [BREAKING] lines | harness-scoped |")
print("| --- | --- | --- | --- |")
tot_b = tot_h = 0
harness_lines = []
for r in rels:
    body = r.get("body") or ""
    breaking = [l.strip() for l in body.splitlines() if "[BREAKING]" in l]
    harness = [l for l in breaking if re.search(r"harness", l, re.I)]
    tot_b += len(breaking)
    tot_h += len(harness)
    for l in harness:
        harness_lines.append(f"- {r['tag_name']}: {l}")
    print(f"| {r['tag_name']} | {r['published_at'][:10]} | {len(breaking)} | {len(harness)} |")

print(f"\nTOTAL across {len(rels)} releases: {tot_b} [BREAKING] lines, {tot_h} mentioning 'harness' (strict keyword match).")
print("\nHarness-keyword lines:")
print("\n".join(harness_lines) if harness_lines else "(none)")
