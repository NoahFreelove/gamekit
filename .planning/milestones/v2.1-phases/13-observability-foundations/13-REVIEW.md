---
phase: 13-observability-foundations
reviewed: 2026-06-14T00:00:00Z
depth: standard
files_reviewed: 11
files_reviewed_list:
  - src/GameKit.Build/PiiAttributeAnalyzer.cs
  - src/GameKit.Core/Telemetry/GameKitTelemetry.cs
  - src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs
  - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
  - src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs
  - src/GameKit.Rankings/Services/RankingsTickerService.cs
  - src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs
  - Directory.Build.props
  - Directory.Packages.props
  - samples/TicTacToeDuel/docker-compose.observability.yml
  - samples/TicTacToeDuel/observability/otel-collector-config.yml
findings:
  critical: 1
  warning: 5
  info: 4
  total: 10
status: issues_found
---

# Phase 13: Code Review Report

**Reviewed:** 2026-06-14
**Depth:** standard
**Files Reviewed:** 11
**Status:** issues_found

## Summary

Phase 13 ships the observability foundations: a Roslyn PII span-attribute analyzer
(`GK0001`/`GK0002`), a `GameKitTelemetry` constants class, an opt-in
`AddGameKitObservability()` builder, canonical `ActivitySource`s for Rankings and
Matchmaking, and a self-hosted OTel/Prometheus/Tempo/Grafana sample stack.

Overall the wiring is careful: image tags are pinned (no `:latest`), Prometheus is
host-isolated, `OpenTelemetry.*` packages carry `PrivateAssets="all"`, the OTel package
graph is consistently pinned at 1.15.3 to dodge the advisories, and the ticker
refactors are pure tag-rename / source-extraction changes (verified against the diff
base). The `GameKit.Build` analyzer project builds clean (no RS1035 violations from its
`System.IO`/`Regex.Compiled` use).

The headline problem is in the analyzer that is the entire point of the phase. The PII
denylist tokenizer splits keys only on `.` and camel/Pascal boundaries — **not on
underscores or hyphens** — so snake_case / kebab-case keys such as `player_id`,
`user_name`, `ip_address`, `client-ip`, and `user-email` pass the analyzer cleanly and
emit PII. Because OpenTelemetry/Prometheus attribute conventions are overwhelmingly
snake_case, this bypass is highly likely to be hit in real consumer code and silently
defeats the GDPR guardrail (OBS-07). That is the one BLOCKER. The remaining findings are
robustness / consistency issues plus a latent build-break trap from `GK0002` interacting
with `TreatWarningsAsErrors`.

## Critical Issues

### CR-01: PII denylist tokenizer does not split on `_` or `-`, allowing snake_case / kebab-case PII keys to bypass GK0001

**File:** `src/GameKit.Build/PiiAttributeAnalyzer.cs:230-249` (`Tokenize`), regex at `91-93`

**Issue:** The token-split algorithm splits the key only on `.` (line 233) and on
camelCase/PascalCase boundaries (line 241 via `CaseBoundaryRegex`). It never splits on
underscore or hyphen. As a result, any whole word that joins a PII token to another word
with `_` or `-` is treated as a single opaque token, fails the whole-token denylist match,
and is emitted as a span attribute.

Verified empirically by running the exact split + denylist logic:

```
player_id      -> [player_id]    FLAGGED=False   (expected: contains "player")
user_name      -> [user_name]    FLAGGED=False   (expected: contains "user")
ip_address     -> [ip_address]   FLAGGED=False   (expected: contains "ip")
client-ip      -> [client-ip]    FLAGGED=False   (expected: contains "ip")
player.id      -> [player, id]   FLAGGED=True    (this case works)
clientIp       -> [client, ip]   FLAGGED=True    (this case works)
```

snake_case is the dominant convention for OpenTelemetry / Prometheus attribute and metric
names (the phase's own Grafana panels and metric names use it, e.g.
`matchmaking_analytics_dropped_events`, `phase.hash_fanout_ms`). A consumer naming a tag
`player_id` — the single most obvious PII key — sails straight through the gate the phase
exists to enforce (OBS-07 / D-07, "never log/tag PII"). This is a security-control bypass,
not a style nit.

**Fix:** Extend the split set to include `_` and `-` alongside `.` before the
case-boundary split:

```csharp
private static IEnumerable<string> Tokenize(string key)
{
    // Step 1: split on dot, underscore, and hyphen — the three separators that
    // routinely join PII tokens to neighbours in OTel/Prometheus attribute keys.
    foreach (var part in key.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries))
    {
        // Step 2: split on PascalCase/camelCase boundaries.
        foreach (var token in CaseBoundaryRegex.Split(part))
        {
            if (!string.IsNullOrEmpty(token))
                yield return token.ToLowerInvariant();
        }
    }
}
```

Add regression test cases for `player_id`, `user-name`, `ip_address`, and `client-ip`
to `PiiAttributeAnalyzerTests` (currently every GK0001 test case uses `.` or camelCase
only, which is exactly why this gap shipped undetected).

## Warnings

### WR-01: `GK0002` is a default-enabled Warning that `TreatWarningsAsErrors=true` promotes to a build-breaking error on any legitimate non-literal tag key

**File:** `src/GameKit.Build/PiiAttributeAnalyzer.cs:68-77` (NonLiteralRule) + `Directory.Build.props:8`

**Issue:** `NonLiteralRule` is declared `defaultSeverity: DiagnosticSeverity.Warning`,
`isEnabledByDefault: true`. The analyzer is referenced by every `src/GameKit.*` project
(via `OutputItemType="Analyzer"`), and `Directory.Build.props` sets
`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. `TreatWarningsAsErrors=true`
promotes *all* warnings to errors regardless of the additive `WarningsAsErrors=CS1591;nullable`
list. Therefore the first time any GameKit package legitimately calls
`activity.SetTag(someStringVariable, value)` — e.g. iterating a dictionary of tags, or
forwarding a caller-supplied dimension — the build will hard-fail with GK0002, even
though the key may be entirely non-PII. Today no src code uses a non-literal key so this
is latent, but it is a foot-gun baked into the shared build: a Warning that is really an
Error-for-everyone.

**Fix:** Either (a) explicitly exempt GK0002 from warning-as-error so it stays a true
warning (`<WarningsNotAsErrors>GK0002</WarningsNotAsErrors>` in `Directory.Build.props`),
or (b) make the intent explicit by setting GK0002 to `DiagnosticSeverity.Info` if it is
guidance rather than a gate. Document the chosen behavior next to the rule so the
"warning vs error" intent is unambiguous.

### WR-02: `IsActivityTagMethod` advertises `ActivityTagsCollection.Add` support but the method-name pre-filter rejects it first (dead branch)

**File:** `src/GameKit.Build/PiiAttributeAnalyzer.cs:118` + `185-199`

**Issue:** `AnalyzeInvocation` returns early at line 118 unless the method name is exactly
`"SetTag"` or `"AddTag"`. But the documented/intended `ActivityTagsCollection` tag-add API
is `ActivityTagsCollection.Add(key, value)` — method name `"Add"`, not `"SetTag"`/`"AddTag"`
(confirmed by the test harness stub at `PiiAttributeAnalyzerTests.cs:47-50`, which declares
`ActivityTagsCollection.Add`). So the `IsActivityTagMethod` check for
`global::System.Diagnostics.ActivityTagsCollection` (line 198) and the XML-doc claim that
the analyzer handles `ActivityTagsCollection.Add` (lines 186-188) are unreachable: any
`.Add(...)` call is filtered out at line 118 before the semantic model is consulted. PII
written via `tags.Add("player.id", ...)` on an `ActivityTagsCollection` (or via the
`ActivityTagsCollection` passed to `ActivitySource.StartActivity(..., tags)`) is NOT
analyzed.

**Fix:** Include `"Add"` in the method-name pre-filter, then rely on the existing
`IsActivityTagMethod` semantic narrowing to keep `Add` scoped to `ActivityTagsCollection`
(so unrelated `List.Add` etc. are not flagged):

```csharp
if (methodName != "SetTag" && methodName != "AddTag" && methodName != "Add")
{
    return;
}
```

Add a GK0001 test for `tagsCollection.Add("player.id", ...)` to lock the behavior in.

### WR-03: Allow-list match is whole-key-exact and case-sensitive, so the documented case-insensitive denylist split and the case-sensitive allow-list disagree

**File:** `src/GameKit.Build/PiiAttributeAnalyzer.cs:172` + `255-287`

**Issue:** The denylist match is fully case-insensitive (tokens are lowercased at
`Tokenize`, line 245), so `Player.Id`, `PLAYER.ID`, and `player.id` are all flagged
identically. But the allow-list comparison uses `StringComparison.Ordinal` on the raw key
(line 279). Consequence: if an operator adds `player.id` to `pii-allowlist.txt` to exempt
it, a flagged call written as `Player.Id` (which the denylist *does* catch) is NOT
exempted, because `"Player.Id" != "player.id"` ordinally. The exemption mechanism is
therefore inconsistent with the detection mechanism — an operator can confirm a key is
"safe", allow-list it, and still get GK0001 on a differently-cased spelling of the same
key. This is a correctness/usability defect in the security-exemption path.

**Fix:** Decide on one canonical casing. Simplest: compare the allow-list entries
case-insensitively (`StringComparison.OrdinalIgnoreCase`) so the exemption matches the
case-insensitive detection. The allow-list file comment at `pii-allowlist.txt:19` claims
"case-sensitive matches" — if that is genuinely desired, the denylist detection should
also be case-sensitive, but that would weaken the guardrail, so prefer making the
allow-list case-insensitive.

### WR-04: `OtlpEndpoint` is parsed with `new Uri(...)` and no validation, throwing an unhelpful `UriFormatException` at startup on malformed input

**File:** `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs:111` + `124`

**Issue:** `OtlpEndpoint` is a free-form `string?` (line 30) and is passed directly to
`new Uri(otlpEndpoint)` inside the tracing and metrics exporter callbacks (lines 111, 124).
A non-null but malformed value (e.g. `"localhost:4317"` with no scheme, a typo, or an
env-var that resolved to empty-after-trim) throws `UriFormatException` deep inside OTel
SDK initialization, with no indication that the GameKit `OtlpEndpoint` option is the
culprit. Worse, the `Protocol` is hardcoded to `OtlpExportProtocol.Grpc` (lines 112, 125)
while the XML-doc example and the sample stack both surface an HTTP endpoint (`:4318`);
an operator who points `OtlpEndpoint` at the `:4318` HTTP port will get silent
export failures because the protocol mismatches the port.

**Fix:** Validate up front with a clear message, and consider exposing the protocol:

```csharp
Uri? exporterUri = null;
if (opts.OtlpEndpoint is not null)
{
    if (!Uri.TryCreate(opts.OtlpEndpoint, UriKind.Absolute, out exporterUri))
    {
        throw new ArgumentException(
            $"GameKitObservabilityOptions.OtlpEndpoint '{opts.OtlpEndpoint}' is not a valid absolute URI " +
            "(expected e.g. 'http://localhost:4317').", nameof(configure));
    }
}
```

Reuse `exporterUri` in both exporter callbacks, and document that the hardcoded protocol
is gRPC (so operators target `:4317`, not `:4318`).

### WR-05: OTel Collector pipeline has no `batch`/`memory_limiter` processors, so a host pushing telemetry can drive unbounded collector memory

**File:** `samples/TicTacToeDuel/observability/otel-collector-config.yml:22-29`

**Issue:** Both pipelines (`traces`, `metrics`) declare `receivers` and `exporters` but no
`processors`. The OTel Collector's own documented baseline strongly recommends at least a
`memory_limiter` (to bound RAM and shed load under pressure) and a `batch` processor (to
avoid per-span export round-trips). Without `memory_limiter`, a burst from the
host-running app — or Tempo being slow/unreachable — can grow collector queue memory
unbounded until OOM. For a sample stack this is "works on a laptop," but it is published
as the reference self-hosted configuration operators will copy, and the missing
`memory_limiter` is a real availability footgun once load is non-trivial.

**Fix:** Add the standard processors and place `memory_limiter` first in each pipeline:

```yaml
processors:
  memory_limiter:
    check_interval: 1s
    limit_mib: 256
    spike_limit_mib: 64
  batch:
    timeout: 5s
    send_batch_size: 1024

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlp/tempo]
    metrics:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [prometheus]
```

## Info

### IN-01: Grafana dashboards query Prometheus metrics that the emitted span tags do not produce

**File:** `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json:30-31,110,146`,
`matchmaking-queue-depth.json:30,60,90,126`

**Issue:** The dashboards query Prometheus series such as
`gamekit_matchmaking_tick_duration_ms_bucket`, `gamekit_matchmaking_matches_formed_total`,
and `gamekit_matchmaking_lease_acquired_total`. The ticker emits these only as **span
tags** (`matches.formed`, `phase.total_ms`, `budget.bail`) on traces routed to Tempo —
not as Prometheus **metrics**. The only real Meter today is `MatchmakingMeter`
(`matchmaking.analytics.dropped_events`), and even that would surface in Prometheus as
`matchmaking_analytics_dropped_events_events_total` (the `events` unit suffix), not the
`matchmaking_analytics_dropped_events_total` the panel queries. The dashboards therefore
render "No data" by construction. The README (`observability/README.md:51-53`) and panel
descriptions explicitly say "No data until Phase 15," so this is documented placeholder
intent rather than a defect — flagged so the Phase 15 work item to reconcile metric names
(including the unit suffix) is not lost.

**Fix:** When Phase 15 wires the metrics, confirm the exported Prometheus series names
(including OTel unit suffixes like `_events`) match the dashboard `expr` strings exactly,
or update the panels.

### IN-02: `matchmaking-queue-depth.json` "Matches Formed" stat uses an inverted threshold (green below red at value 0)

**File:** `samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json:97-103`

**Issue:** Panel 3's thresholds are `green @ null` then `red @ 0`, meaning the displayed
value turns red at >= 0 — i.e., zero or more matches reads as red while the `null`/no-data
state is green. That is backwards for a "matches formed" health stat (you want red when
*no* matches form). Cosmetic only (placeholder dashboard), but the threshold direction is
wrong as written.

**Fix:** Use `red @ null/0`, `green @ 1` (or drop the threshold and use a fixed color),
so an absence of matches reads as the alerting color.

### IN-03: `GameKitObservabilityOptions` XML doc claims "all fields are optional" but the type exposes exactly one field

**File:** `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs:17-21`

**Issue:** The remarks say "All fields are optional," which is true but reads as if there
are several. Minor doc-accuracy nit; harmless. Worth a wording pass to "The single
`OtlpEndpoint` field is optional" so the public API doc does not over-promise a richer
options surface than exists.

**Fix:** Reword the `<remarks>` to reference the one option by name.

### IN-04: Grafana runs with anonymous Admin access and no note pinning it to the non-published profile

**File:** `samples/TicTacToeDuel/docker-compose.observability.yml:43-49`

**Issue:** Grafana is configured with `GF_AUTH_ANONYMOUS_ENABLED=true` and
`GF_AUTH_ANONYMOUS_ORG_ROLE=Admin`, and its `:3000` port *is* published to the host. The
README (`observability/README.md:57-58`) correctly labels this "local dev convenience
only — not suitable for production," so this is acceptable for a sample. Flagged only
because, unlike Prometheus (deliberately host-isolated), Grafana's anonymous-Admin surface
*is* reachable from the host — an operator who copies this compose file into a shared/CI
host exposes an unauthenticated Admin Grafana. No secret is committed (the Postgres
`postgres_bootstrap_dev_only` value is clearly a dev placeholder), so this stays Info.

**Fix:** Add an inline comment on the `grafana` service mirroring the README warning, so
the "dev-only, do not expose" caveat travels with the compose file itself.

---

_Reviewed: 2026-06-14_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
