# A reference pack for a runtime the feed has not published yet

## Why

Every PR failed on 2026-10-25 with `no Microsoft.NETCore.App.Ref is published for runtime 10.0.14 on (local); a reference pack is required to compile` — on all four legs, including a documentation-only branch. The CI runners had picked up a **staged** .NET runtime (10.0.14) before its reference pack was published; the rule rejected an older pack on purpose, so nothing could compile and the repository went red for a reason no change in it caused.

The same shape reaches users: anyone running a .NET patch ahead of the published reference pack cannot compile an extension at all, and the message blames the feed rather than offering the one thing that works.

## What changes

- The reference-pack rule gains a **last resort**: when no pack at or above the running runtime is published, the highest published pack of the same major and minor **below** it is chosen, recorded in the lock as `nearest-below-runtime`.
- The preference order is unchanged where packs exist: exact runtime version, then the lowest pack at or above it, then — only when the feed has nothing at or above — the highest below.
- The rule stays a hard failure when the runtime's own major/minor has no pack at all, because a pack from another minor cannot describe the API.

## Why this is the right compromise

A patch release is API-compatible by construction: the pack below the runtime describes a subset of the surface, so compiling against it compiles code that runs. The alternative — refusing — makes an environment detail (which runtime the machine happens to have) decide whether extensions work. The lock records the rule, so the compromise is visible in `/extensions` and in the lock file rather than silent, and the toolset half is outside the trust digest, so nothing re-prompts.

## Impact

`src/Lunate.Extensibility.Restore/ReferencePackRule.cs` and one constant in `src/Lunate.Extensibility/ExtensionLock.cs`; two tests updated, three added. One MODIFIED requirement (`extensions`: *Reference assemblies for compilation*) with the preference order and the two cases spelled out.
