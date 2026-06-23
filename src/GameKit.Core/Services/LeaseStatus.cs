// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Core.Services;

/// <summary>Snapshot of a distributed leader lock: current holder + TTL.</summary>
/// <param name="HolderInstanceId">The holder's <c>InstanceId</c>, or <c>null</c> when unheld.</param>
/// <param name="Ttl">Remaining lease duration, or <c>null</c> when the key has no TTL.</param>
public sealed record LeaseStatus(string? HolderInstanceId, TimeSpan? Ttl);
