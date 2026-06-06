// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Auth.Argon2.Configuration;
using GameKit.Auth.Argon2.Services;
using GameKit.Auth.Services;
using GameKit.Core.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GameKit.Auth.Argon2.Builder;

/// <summary>
/// Extension methods on <see cref="IGameKitBuilder"/> for opting in to the Argon2id password hasher.
/// </summary>
public static class Argon2BuilderExtensions
{
    /// <summary>
    /// Replaces the default <see cref="BCryptPasswordHasher"/> with <see cref="Argon2idPasswordHasher"/>
    /// using Argon2id (OWASP-recommended). Must be called AFTER <c>.AddAuth(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The existing <see cref="IPasswordHasher"/> singleton registered by <c>AddAuth()</c> is removed
    /// and replaced so exactly one hasher is active at runtime.
    /// </para>
    /// <para>
    /// The <see cref="Argon2idPasswordHasher"/> also verifies legacy BCrypt hashes (<c>$2a$</c>/<c>$2b$</c>)
    /// so that a live BCrypt→Argon2 migration can proceed transparently. See AUTH-18.
    /// </para>
    /// <para>
    /// WR-01: <see cref="GameKitArgon2Options.AllowInsecureParametersForTesting"/> is only honoured
    /// in Development environments. A startup <see cref="IHostedService"/> (registered here) throws
    /// <see cref="InvalidOperationException"/> before Kestrel accepts any traffic when the flag is
    /// <see langword="true"/> and the host environment is not Development.
    /// </para>
    /// </remarks>
    /// <param name="builder">The GameKit builder returned from <c>AddAuth()</c>.</param>
    /// <param name="configure">Optional delegate to customise Argon2 parameters (memory cost, time cost, etc.).</param>
    /// <returns>The same <see cref="IGameKitBuilder"/> for fluent chaining.</returns>
    public static IGameKitBuilder UseArgon2(
        this IGameKitBuilder builder,
        Action<GameKitArgon2Options>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var opts = new GameKitArgon2Options();
        configure?.Invoke(opts);

        // WR-02: Enforce OWASP 2025 minimum parameter floors at registration time.
        // Isopoh.Argon2 accepts any parameter values without a floor of its own; a
        // misconfigured UseArgon2() call would silently produce negligibly weak hashes.
        // Throwing here at startup is far better than silently under-protecting passwords.
        // AllowInsecureParametersForTesting bypasses these guards for integration tests only.
        const int OwaspMinMemoryCostKib = 19456;
        const int OwaspMinTimeCost      = 2;
        const int OwaspMinLanes         = 1;

        if (!opts.AllowInsecureParametersForTesting)
        {
            if (opts.MemoryCost < OwaspMinMemoryCostKib)
                throw new ArgumentOutOfRangeException(
                    nameof(configure),
                    $"GameKitArgon2Options.MemoryCost ({opts.MemoryCost} KiB) is below the OWASP 2025 " +
                    $"minimum ({OwaspMinMemoryCostKib} KiB). Use a higher value in production; " +
                    "set MemoryCost >= 19456 or reduce only in an explicit test environment.");

            if (opts.TimeCost < OwaspMinTimeCost)
                throw new ArgumentOutOfRangeException(
                    nameof(configure),
                    $"GameKitArgon2Options.TimeCost ({opts.TimeCost}) is below the OWASP 2025 " +
                    $"minimum ({OwaspMinTimeCost} iterations).");

            if (opts.Lanes < OwaspMinLanes)
                throw new ArgumentOutOfRangeException(
                    nameof(configure),
                    $"GameKitArgon2Options.Lanes ({opts.Lanes}) must be at least {OwaspMinLanes}.");
        }

        builder.Services.AddSingleton(opts);

        // Remove BCryptPasswordHasher registered by AddAuth(); replace with Argon2idPasswordHasher.
        // RemoveAll<T>() is safe to call even if the registration is absent.
        builder.Services.RemoveAll<IPasswordHasher>();
        builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();

        // WR-01: Register a startup guard that throws InvalidOperationException when
        // AllowInsecureParametersForTesting is set outside a Development environment.
        // Runs at IHost.StartAsync before Kestrel accepts any traffic.
        builder.Services.AddHostedService<Argon2InsecureParamGuardHostedService>();

        return builder;
    }
}
