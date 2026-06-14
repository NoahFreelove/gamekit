---
phase: 13
slug: observability-foundations
status: verified
threats_open: 0
asvs_level: 1
created: 2026-06-14
---

# Phase 13 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

This register was authored at plan time — all four 13-0x-PLAN.md files carried a
`<threat_model>` block. The audit below verifies each plan-time mitigation exists in the
implementation (mitigate dispositions) or is documented (accept dispositions). One
additional threat (GRAFANA-ANON) was surfaced during execution (SUMMARY 13-04 / review
IN-04) and is recorded as an accepted risk.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| developer source → telemetry backend | Span attribute keys written in `src/` flow into traces an operator can inspect | Potentially PII if a key like `player_id`/`email` is tagged |
| nuget.org → repo build | Two analyzer-testing packages pulled into the build (supply-chain entry point) | Build-time analyzer test dependencies |
| GameKit.Core NuGet → consumer build graph | OTel SDK packages added to Core must NOT flow to consumers who skip `AddGameKitObservability()` | Transitive package surface |
| per-package telemetry class → Core constants | Divergence from `GameKitTelemetry` would reintroduce magic strings / silent drift | Source/meter names, attribute keys |
| host network → container ports | Any container port published to the host is reachable by anything on the host | App metrics (Prometheus) must stay host-isolated |
| docker registry → operator pull | Image tags determine exactly what code runs; `:latest` is a tampering/supply-chain vector | Container images |
| sample app (host) → Collector `:4317` | Unauthenticated OTLP push on localhost — acceptable for a dev-only sample | Telemetry (traces/metrics) |

---

## Threat Register

| Threat ID | Category | Component | Disposition | Mitigation | Status |
|-----------|----------|-----------|-------------|------------|--------|
| T-13-PII | Information Disclosure | `SetTag`/`AddTag`/`ActivityTagsCollection.Add` keys in `src/` | mitigate | `GK0001` analyzer (Error) blocks player/user/email/token/ip/fingerprint at build time; tokenizer splits on `.`/`_`/`-` and camel/Pascal boundaries (CR-01 snake_case/kebab-case bypass fixed, commit 7ae9aee); 13/13 `PiiAttributeAnalyzerTests` incl. `player_id`/`client-ip` regressions; wired solution-wide via `Directory.Build.props` `AdditionalFiles` | closed |
| T-13-SC | Tampering | `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing(.XUnit)` install | mitigate | slopcheck + blocking-human checkpoint verified both packages against dotnet/roslyn-analyzers before the `Directory.Packages.props` edit; both pinned at `1.1.2` | closed |
| T-13-DEP-FLOW | Elevation of Privilege (unwanted dep surface) | `GameKit.Core.csproj` OTel `PackageReference`s | mitigate | `PrivateAssets="all"` on both `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol` — SDK does not flow to consumers who skip the opt-in (csproj lines 40–41) | closed |
| T-13-MAGIC | Tampering (silent drift) | per-package vs Core source/meter-name divergence | mitigate | Reflection enforcement test asserts `MatchmakingActivitySource.SourceName == GameKitTelemetry.MatchmakingTickerSourceName` (and Meter name) at runtime; cross-cutting attr keys route through `GameKitTelemetry` consts; camelCase source-asserts all zero | closed |
| T-13-METRICS | Information Disclosure | Prometheus service port | mitigate | `prometheus:` service has NO `ports:` mapping — host-isolated on `obs-internal` (explicit comment in compose); ISOLATION-OK acceptance criterion (`curl localhost:9090` MUST-FAIL) confirmed in SUMMARY 13-04 | closed |
| T-13-SC-IMG | Tampering / Supply chain | Docker image tags | mitigate | Every image tag pinned explicitly (otel-collector-contrib:0.154.0, prometheus:v3.11.2, tempo:2.6.1, grafana:13.0.2, postgres:17.9, redis:8.6.2); `grep -c ':latest'` == 0 | closed |
| T-13-PII-FN | Information Disclosure | non-literal / dynamic tag keys | accept (with signal) | `GK0002` Warning surfaces dynamic keys the analyzer cannot statically evaluate; recommended upgrade to Error after const migration (Phase 15). Accepted as Warning for v1. See Accepted Risks. | closed |
| T-13-RENAME | Tampering (operator dashboards) | attribute key rename (Matchmaking/Rankings) | accept | Spans are no-ops until subscribed; project not yet public; zero operator dashboard queries exist — rename safe to ship now (RESEARCH §Safety of Renaming). See Accepted Risks. | closed |
| T-13-OTLP | Spoofing / Tampering | OTLP endpoint on `localhost:4317` (unauthenticated) | accept | Dev-only sample stack; no mTLS locally; README documents production must secure the OTLP endpoint. See Accepted Risks. | closed |
| T-13-AGPL | Licensing (distribution obligations) | Tempo + Grafana AGPLv3 | accept | Operator-pulled containers; GameKit neither links nor distributes them; README §Jaeger Swap documents an Apache-2.0 alternative. See Accepted Risks. | closed |
| GRAFANA-ANON | Information Disclosure | Grafana service (`GF_AUTH_ANONYMOUS_ENABLED=true`, role `Admin`) on published `:3000` | accept | Surfaced during execution (SUMMARY 13-04 / review IN-04). Grants anonymous full admin in Grafana — acceptable for dev-only local sample, reachable from host. README labels it "local dev convenience only — not suitable for production." See Accepted Risks. | closed |

*Status: open · closed*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-13-01 | T-13-PII-FN | Dynamic/non-literal tag keys cannot be statically evaluated; `GK0002` Warning signals them without hard-breaking legitimate dynamic keys. Upgrade to Error tracked for Phase 15 after const migration. | Phase 13 plan (13-01) | 2026-06-14 |
| AR-13-02 | T-13-RENAME | Attribute-key renames are safe pre-1.0: spans are no-ops until a host subscribes, the project is not yet public, and no operator dashboard queries the old keys. | Phase 13 plan (13-03) | 2026-06-14 |
| AR-13-03 | T-13-OTLP | Unauthenticated OTLP on `localhost:4317` is acceptable for the dev-only sample stack; README instructs operators to secure the endpoint (mTLS/auth) for production deployments. | Phase 13 plan (13-04) | 2026-06-14 |
| AR-13-04 | T-13-AGPL | Tempo and Grafana are AGPLv3, but operator-pulled as containers — GameKit does not link or distribute them, so no GPL-incompatibility. README documents a one-line Jaeger (Apache-2.0) swap for operators who prefer to avoid AGPLv3. | Phase 13 plan (13-04) | 2026-06-14 |
| AR-13-05 | GRAFANA-ANON | Anonymous-Admin Grafana on published `:3000` is a deliberate dev-convenience for the local sample; README warns it is "not suitable for production." No secret committed (Postgres value is a clear dev placeholder). Operators copying the compose file to a shared/CI host must disable anonymous auth. | Phase 13 execution (review IN-04) | 2026-06-14 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-06-14 | 11 | 11 | 0 | Claude (gsd-secure-phase, short-circuit verify) |

**Audit method:** `register_authored_at_plan_time: true` (all 4 PLAN files carried a
`<threat_model>` block) and `threats_open: 0` → short-circuit verification. Each mitigation
was confirmed first-hand against the implementation (analyzer source, csproj `PrivateAssets`,
compose `ports`/image tags, `Directory.Packages.props` pins, reflection test, README
accepted-risk docs) rather than scanning for new threats. Cross-checked against
13-VERIFICATION.md (5/5 truths passed) and 13-REVIEW.md (CR-01 PII bypass resolved in
commit 7ae9aee).

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-06-14
