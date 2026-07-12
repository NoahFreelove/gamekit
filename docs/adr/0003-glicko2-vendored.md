<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0003: Glicko-2 algorithm vendored, not a NuGet dependency

**Status:** Accepted

## Context

`GameKit.Rankings` provides a default `IRankingAlgorithm` implementation based
on the Glicko-2 rating system (Glickman, 2012). Several C# Glicko-2
implementations exist on NuGet:

- `Glicko-2RankingSystem` — last updated ~2016; no source-link; no tests.
- `Glicko2` — similar vintage; unmaintained.
- `MaartenStaa/glicko2-csharp` (GitHub only, no NuGet package) — a clean
  reference port with attribution to Glickman's original paper and a worked
  example from the PDF.

None of the NuGet packages is actively maintained as a library. They are all
thin ports of the ~150-line algorithm from Glickman's 2012 paper. Taking a NuGet
dependency on an unmaintained package adds upgrade friction and security-audit
noise without any reuse benefit — the algorithm itself is a fixed mathematical
specification, not software that evolves.

The `MaartenStaa/glicko2-csharp` implementation is licensed under BSD-3-Clause,
which permits incorporation into a Apache-2.0 library with attribution.

## Decision

The Glicko-2 algorithm is vendored into `GameKit.Rankings` under
`src/GameKit.Rankings/Glicko2/` with:

1. Attribution to `MaartenStaa/glicko2-csharp` in the source file headers.
2. A regression fixture asserting Glickman's original worked example from the
   PDF (the three-player example on glicko.net) passes exactly.
3. License: the vendored files retain their original BSD-3-Clause attribution;
   the surrounding `GameKit.Rankings` package is Apache-2.0 (compatible).

No NuGet dependency on any Glicko-2 package is added.

## Consequences

- **Positive:** No external dependency to upgrade or audit for CVEs. The algorithm
  is a fixed mathematical specification — it does not change. The vendored copy is
  100 % under our review and test coverage.
- **Positive:** The regression fixture (Glickman's worked example) makes any
  accidental algorithm modification immediately visible.
- **Negative:** We own the algorithm code. Any future corrections to the Glicko-2
  formulation must be applied manually. In practice the 2012 formulation is final.
- **Consumers:** `IRankingAlgorithm` is a public interface — consumers can replace
  the Glicko-2 default with any algorithm (ELO, TrueSkill-derived, etc.) by
  registering a custom `IRankingAlgorithm` via the `IGameKitRankingsBuilder`.
