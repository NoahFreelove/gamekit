<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0005: MinVer for versioning, not Nerdbank.GitVersioning or GitVersion

**Status:** Accepted

## Context

GameKit ships every `src/` project as its own NuGet package. All packages share a
single version (coupled release train): a consumer who pins `GameKit.Core@1.2.0`
can pin every sibling to `1.2.0`. This requires a single version source that
applies uniformly across the repository.

Three candidates were evaluated:

**Nerdbank.GitVersioning (NBGv):** Uses a JSON config file (`version.json`) plus
Git commit height to produce the patch number (e.g., `1.2.47`). The commit-height
approach means that every commit on every branch increments the patch version,
producing version gaps — `1.2.0` may never be released if `1.2.1` exists by the
time the release is cut. The JSON file is an additional source of truth alongside
the Git tag. Shallow CI clones require extra configuration.

**GitVersion:** Powerful, branch-model-aware versioning (GitFlow, GitHub Flow,
trunk-based). However: notoriously slow (full `git log` walk), historically
fragile on shallow CI clones, and requires a substantial configuration file. Overkill
for a library with a simple linear release model.

**MinVer:** Tag-driven SemVer. The version is the nearest ancestor `v*` tag on the
Git graph. No JSON file, no commit height, no config file beyond a single
`<MinVerTagPrefix>v</MinVerTagPrefix>` in `Directory.Build.props`. The RC → RTM
workflow is trivially clean: tag the same commit `v1.0.0-rc.1`, publish; later tag
`v1.0.0`, publish — no version gaps.

## Decision

MinVer 7.0.0 is the sole versioning tool, configured via `Directory.Build.props`:

```xml
<PackageReference Include="MinVer" Version="7.0.0" PrivateAssets="all" />
<MinVerTagPrefix>v</MinVerTagPrefix>
```

All packages share the MinVer-derived version. Source Link (GitHub) is also
wired in `Directory.Build.props` so that the version and symbols are aligned.

## Consequences

- **Positive:** Single source of truth (the Git tag). No version gaps. Zero
  configuration. CI and local builds produce identical versions for the same tag.
- **Positive:** RC/pre-release workflow is natural: `v1.0.0-rc.1`, `v1.0.0-rc.2`,
  `v1.0.0`. Consumers can pin to a pre-release via standard NuGet float notation.
- **Negative:** If a branch diverges far from the latest tag without a tag, the
  version becomes `0.0.0-alpha.0.N` (MinVer fallback). This is only a concern
  during active development before v1.0.0 is tagged — no operational impact.
- **Negative:** All packages share a version. A patch fix in `GameKit.Auth` bumps
  the version for `GameKit.Core` too. This is a deliberate simplification for v1 —
  re-evaluate per-package versioning if packages diverge significantly in stability
  or release cadence.
