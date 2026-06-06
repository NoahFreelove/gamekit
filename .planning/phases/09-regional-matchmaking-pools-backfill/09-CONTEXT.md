# Phase 9: Regional Matchmaking Pools + Backfill - Context

**Gathered:** 2026-06-06
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Regional matchmaking pools are a first-class concept (no schema migration needed), and backfill into in-progress sessions ships with the participation-fraction guard in the same unit.

**Depends on:** Phase 8 (Matchmaking enqueue path was modified for real ratings in Phase 8; Phase 9 extends the same enqueue path with RegionName; backfill ticket type reads `ParticipationFraction` which is a new column requiring Phase 8's migration pass to be stable first).

**Requirements:** MATCH-18, MATCH-19

**Success Criteria (what must be TRUE):**
1. A developer configuring `AllowedRegions = ["us-east", "eu-west"]` on a ladder sees enqueue requests with a mismatched or missing `RegionName` rejected with a validation error; a `RegionName = null` request routes to the `"default"` pool (backwards-compatible v1 behaviour).
2. The Redis queue key for a regional pool is `mm:queue:{ladderId}:{regionName}` and is distinct from the default `mm:queue:{ladderId}:default`; the ticker's existing pool-scan glob picks up both keys without any ticker code changes.
3. A `POST /api/matchmaking/backfill` request creates a `backfill`-typed ticket; the backfill ticket is processed at higher priority than normal tickets.
4. A backfill player whose `ParticipationFraction` falls below the configured minimum does not receive a rating change — an integration test confirms the `IRankingAlgorithm.Apply` guard fires correctly.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices are at Claude's discretion — discuss phase was skipped per user setting. Use ROADMAP phase goal, success criteria, and codebase conventions to guide decisions.

</decisions>

<code_context>
## Existing Code Insights

Codebase context will be gathered during plan-phase research.

</code_context>

<specifics>
## Specific Ideas

No specific requirements — discuss phase skipped. Refer to ROADMAP phase description and success criteria.

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
