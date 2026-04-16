// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using Xunit;

namespace GameKit.TestFixtures;

/// <summary>
/// Layer 2 egress guard: provides a violation counter for tests that exercise GameKit.Core
/// code paths which must never open outbound HTTP connections.
/// </summary>
/// <remarks>
/// <para>
/// In .NET 10 there is no global <c>SocketsHttpHandler.ConnectCallback</c> hook — the callback
/// is per-<c>HttpClient</c> instance. The <strong>reliable</strong> enforcement is Layer 1
/// (assembly-metadata reflection in <c>EgressGuardTests</c>). This fixture exists as a hook
/// point for Phase 2+ when <c>GameKit.Auth</c> introduces its own <c>HttpClient</c> with
/// an allow-list for Steam/Discord provider endpoints.
/// </para>
/// <para>
/// For Phase 1, Core contains zero <c>HttpClient</c> references (verified by Layer 1).
/// This fixture records that zero violations occurred during a test run.
/// </para>
/// </remarks>
public sealed class EgressGuardFixture : IAsyncLifetime
{
    /// <summary>Number of outbound HTTP violations detected.</summary>
    public int ViolationCount { get; private set; }

    /// <summary>Records a violation. Called by test harnesses that intercept outbound connections.</summary>
    public void RecordViolation() => ViolationCount++;

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;
}
