<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0009: OpenTelemetry as opt-in, not a forced dependency

**Status:** Accepted

## Context

GameKit is self-hosted software used in air-gapped environments, regulated
industries, and deployments where any external network egress is prohibited or
must be explicitly approved. A library that forces OpenTelemetry onto every
consumer and defaults to any hard-coded OTLP endpoint would violate the
**air-gap guarantee** — the core promise that no GameKit-instrumented application
sends telemetry to any external service without explicit operator configuration.

### Threat model: information disclosure via OTel

If GameKit forced OTel and shipped a default OTLP endpoint (even `localhost`),
a misconfigured consumer (or a malicious dependency that overrides the endpoint)
could cause telemetry — including player IDs, match IDs, session tokens visible
in trace attributes — to be sent to an unintended destination. This is a
**T-20-05-01 information-disclosure threat** in the Phase 20 threat register.

The correct mitigation is architectural: GameKit's `ActivitySource` and `Meter`
instruments are registered with the .NET `DiagnosticSource` / `Metrics` subsystem
(which are themselves no-ops when no OTel SDK is present). No OTLP exporter is
configured by GameKit. The consumer explicitly calls `AddGameKitObservability()`
and provides the OTLP endpoint (or omits it for SDK-only registration with no
exporter). See [docs/upgrade/v2.0-to-v2.1.md](../upgrade/v2.0-to-v2.1.md) §1
for the exact wiring.

### What "opt-in" means in practice

The OpenTelemetry packages themselves (`OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`)
**are** referenced by `GameKit.Core` — they are required to define the
`ActivitySource` and `Meter` types. What is opt-in is:

1. **OTLP exporter registration** — `AddGameKitObservability()` only wires an
   `OtlpTraceExporter` when `otel.OtlpEndpoint` is non-null. If null, sources
   and meters are registered with the OTel SDK but no exporter is attached.
2. **ASP.NET Core HTTP instrumentation** — GameKit does not call
   `AddAspNetCoreInstrumentation()`. That is a host decision, documented in the
   upgrade guide. Consumers who want HTTP parent spans must opt in.
3. **Propagation headers** — W3C `traceparent` propagation is enabled when the
   OTel SDK is configured. GameKit does not emit or read `traceparent` headers
   outside of the OTel SDK pipeline, so consumers without OTel see no overhead.

## Decision

GameKit uses `ActivitySource` and `Meter` from `System.Diagnostics` (part of the
.NET runtime) for all internal instrumentation, and `OpenTelemetry` packages for
SDK integration. No OTLP exporter is configured by the library. Consumers opt in
to exporter configuration via `AddGameKitObservability(otel => { ... })` with an
explicitly consumer-supplied OTLP endpoint.

ASP.NET Core HTTP instrumentation is a host decision and is never added by GameKit.

## Consequences

- **Positive:** Zero telemetry egress from a GameKit application unless the
  operator explicitly configures `OtlpEndpoint`. The air-gap guarantee holds.
- **Positive:** Consumers who do not want observability overhead pay only the cost
  of the `ActivitySource.StartActivity()` call on the hot path — which is a
  no-op (returns null) when no listener is attached. This is the standard .NET
  diagnostic model used by ASP.NET Core, EF Core, and StackExchange.Redis itself.
- **Positive:** Consumers can bring their own OTel SDK setup and add
  `AddSource("GameKit.*")` to discover GameKit's sources without calling
  `AddGameKitObservability()` at all — the sources are registered regardless.
- **Negative:** Consumers who want observability must add one call to their
  `Program.cs`. This is intentional friction that ensures operator awareness of
  the telemetry configuration.
- **Security note:** The `OtlpEndpoint` configuration key (`GameKit:Observability:OtlpEndpoint`)
  must never be set to an external service URL in production unless that service
  is operator-controlled. Review your appsettings.* files and environment variable
  overrides before deploying to a regulated environment.
