// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Argon2.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameKit.Auth.Argon2.Services;

/// <summary>
/// Startup guard that enforces <see cref="GameKitArgon2Options.AllowInsecureParametersForTesting"/>
/// is never honoured outside a Development environment (WR-01).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GameKitArgon2Options.AllowInsecureParametersForTesting"/> bypasses the OWASP 2025
/// minimum parameter floors enforced by <see cref="GameKit.Auth.Argon2.Builder.Argon2BuilderExtensions.UseArgon2"/>.
/// A production misconfiguration (e.g. the flag set via <c>appsettings.Production.json</c> or an
/// environment variable) would silently hash production passwords with negligibly weak parameters.
/// </para>
/// <para>
/// This hosted service runs at host startup, BEFORE Kestrel accepts any traffic.
/// It throws an <see cref="InvalidOperationException"/> when <c>AllowInsecureParametersForTesting</c>
/// is <see langword="true"/> and the host environment is NOT Development. In Development the flag
/// is allowed so integration tests and local developer builds can use low-cost parameters without
/// waiting for full OWASP-cost Argon2 hashes.
/// </para>
/// </remarks>
internal sealed class Argon2InsecureParamGuardHostedService : IHostedService
{
    private readonly GameKitArgon2Options _opts;
    private readonly IHostEnvironment _env;
    private readonly ILogger<Argon2InsecureParamGuardHostedService>? _logger;

    /// <summary>Constructs the startup guard.</summary>
    /// <param name="opts">The registered <see cref="GameKitArgon2Options"/> singleton.</param>
    /// <param name="env">Host environment — used to determine whether the flag is safe to honour.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public Argon2InsecureParamGuardHostedService(
        GameKitArgon2Options opts,
        IHostEnvironment env,
        ILogger<Argon2InsecureParamGuardHostedService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(env);
        _opts = opts;
        _env = env;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_opts.AllowInsecureParametersForTesting && !_env.IsDevelopment())
        {
            throw new InvalidOperationException(
                "GameKitArgon2Options.AllowInsecureParametersForTesting is set outside a Development " +
                "environment. This flag must not be set in production — it disables OWASP password " +
                "hashing security floors. Remove it from any non-Development configuration.");
        }

        if (_opts.AllowInsecureParametersForTesting)
        {
            _logger?.LogWarning(
                "GameKitArgon2Options.AllowInsecureParametersForTesting is enabled. " +
                "OWASP minimum parameter guards are bypassed. This flag is only safe in " +
                "Development environments.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
