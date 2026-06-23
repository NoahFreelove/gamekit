// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Xunit;

namespace GameKit.DR.Tests;

/// <summary>
/// Serialises all DR round-trip tests so they run sequentially and never share bind-mount
/// temp directories or Testcontainers port allocations. The round-trip test spins two
/// sequential Postgres containers with shared bind mounts — parallel execution would cause
/// port and filesystem contention.
/// </summary>
[CollectionDefinition("DisasterRecovery", DisableParallelization = true)]
public sealed class DisasterRecoveryCollection
{
    // Marker class — no fixture state. The [CollectionDefinition] attribute is the sole purpose.
}
