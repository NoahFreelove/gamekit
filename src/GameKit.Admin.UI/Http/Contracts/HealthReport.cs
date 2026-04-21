// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>Aggregate health report returned by <c>IHealthProbeService.ProbeAsync</c>.</summary>
/// <param name="Postgres">Postgres connectivity tile.</param>
/// <param name="Redis">Redis connectivity tile.</param>
/// <param name="ErrorRate">Recent-error-rate tile (from <c>ErrorRateRingBuffer</c>).</param>
/// <param name="CheckedAt">UTC timestamp at which the probe was taken.</param>
public sealed record HealthReport(
    HealthTile Postgres,
    HealthTile Redis,
    HealthTile ErrorRate,
    DateTimeOffset CheckedAt);

/// <summary>One health tile — status + free-text detail + optional latency.</summary>
/// <param name="Status">Stable status string: <c>OK</c> / <c>Degraded</c> / <c>Down</c>.</param>
/// <param name="Detail">Human-readable short description (e.g. exception class, count).</param>
/// <param name="LatencyMs">Round-trip latency in milliseconds for connectivity probes. Null for gauge-style tiles (e.g. error rate).</param>
public sealed record HealthTile(
    string Status,
    string Detail,
    double? LatencyMs);
