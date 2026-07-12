// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Production default <see cref="IChaosInterceptor"/> implementation — both probes return
/// <see cref="Task.CompletedTask"/> with no allocation and zero runtime cost. Registered via
/// <c>TryAddSingleton</c> so the Plan 05-09 chaos test can override the binding before
/// <c>AddMatchmaking</c> is called.
/// </summary>
/// <remarks>
/// The class name is deliberately verbose so the DI service listing surfaces the no-op semantics
/// explicitly — an operator inspecting the service collection sees
/// <c>IChaosInterceptor → NullChaosInterceptor</c> and immediately understands the
/// interceptor is inert in production (T-05-09-01 mitigation).
/// </remarks>
public sealed class NullChaosInterceptor : IChaosInterceptor
{
    /// <inheritdoc />
    public Task BeforeLuaClaim(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task BeforeSessionInsert(CancellationToken ct) => Task.CompletedTask;
}
