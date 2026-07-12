// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using GameKit.Presence.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameKit.Presence.Tests;

/// <summary>
/// Unit tests for <see cref="PresenceOptionsValidator"/> — covers the 3× safety-factor
/// invariant from CONTEXT D-01 and the lower-bound checks on TtlSeconds /
/// HeartbeatIntervalSeconds. Mirrors the pure-function validator pattern shipped in
/// Plan 05-03 (<c>MatchmakingOptionsValidatorTests</c>).
/// </summary>
public sealed class PresenceOptionsValidatorTests
{
    [Fact]
    public void Defaults_Pass_Validation()
    {
        var opts = new GameKitPresenceOptions();
        var ok = PresenceOptionsValidator.Validate(opts, out var failures);

        Assert.True(ok, $"defaults must validate, got: {Join(failures)}");
        Assert.Empty(failures);
    }

    [Fact]
    public void TtlSeconds_Zero_Fails()
    {
        var opts = new GameKitPresenceOptions { TtlSeconds = 0, HeartbeatIntervalSeconds = 10 };
        var ok = PresenceOptionsValidator.Validate(opts, out var failures);

        Assert.False(ok);
        Assert.Contains(failures, f => f.Contains(nameof(GameKitPresenceOptions.TtlSeconds)));
    }

    [Fact]
    public void HeartbeatIntervalSeconds_Zero_Fails()
    {
        var opts = new GameKitPresenceOptions { TtlSeconds = 30, HeartbeatIntervalSeconds = 0 };
        var ok = PresenceOptionsValidator.Validate(opts, out var failures);

        Assert.False(ok);
        Assert.Contains(failures, f => f.Contains(nameof(GameKitPresenceOptions.HeartbeatIntervalSeconds)));
    }

    [Fact]
    public void SafetyFactor_Violation_Fails_When_3xInterval_Exceeds_Ttl()
    {
        // 3 × 11 = 33 > 30 — violates the 3× safety-factor invariant (CONTEXT D-01).
        var opts = new GameKitPresenceOptions { TtlSeconds = 30, HeartbeatIntervalSeconds = 11 };
        var ok = PresenceOptionsValidator.Validate(opts, out var failures);

        Assert.False(ok);
        Assert.Contains(
            failures,
            f => f.Contains("safety", System.StringComparison.OrdinalIgnoreCase)
                 || f.Contains("3", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Documented_Defaults_Pass_The_3x_Safety_Check()
    {
        // 3 × 10 = 30 == TtlSeconds — exactly the 3× safety boundary; must pass.
        var opts = new GameKitPresenceOptions { TtlSeconds = 30, HeartbeatIntervalSeconds = 10 };
        var ok = PresenceOptionsValidator.Validate(opts, out var failures);

        Assert.True(ok, $"documented default ratio must pass, got: {Join(failures)}");
    }

    [Fact]
    public void IValidateOptions_Surface_Returns_Success_For_Defaults()
    {
        var sut = new PresenceOptionsValidator();
        var result = sut.Validate(name: null, options: new GameKitPresenceOptions());

        Assert.Same(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void IValidateOptions_Surface_Returns_Failures_For_Zero_Ttl()
    {
        var sut = new PresenceOptionsValidator();
        var result = sut.Validate(name: null, options: new GameKitPresenceOptions { TtlSeconds = 0 });

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Failures!);
    }

    private static string Join(IReadOnlyList<string> failures) => string.Join("; ", failures);
}
