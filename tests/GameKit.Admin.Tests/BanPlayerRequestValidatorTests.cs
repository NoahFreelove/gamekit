// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation.TestHelper;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Admin.UI.Http.Validators;
using Xunit;

namespace GameKit.Admin.Tests;

/// <summary>
/// Unit tests for <see cref="BanPlayerRequestValidator"/>. Verifies the D-09 3-512 char reason
/// rule emits the exact error messages the ROADMAP success-criteria anchors depend on.
/// </summary>
public sealed class BanPlayerRequestValidatorTests
{
    [Fact]
    public void EmptyReason_Is_Invalid_With_Required_Message()
    {
        var v = new BanPlayerRequestValidator();
        var result = v.TestValidate(new BanPlayerRequest(string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage("A reason is required.");
    }

    [Fact]
    public void TwoCharReason_Is_Invalid_With_Min_Length_Message()
    {
        var v = new BanPlayerRequestValidator();
        var result = v.TestValidate(new BanPlayerRequest("ab"));
        result.ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage("Reason must be at least 3 characters.");
    }

    [Fact]
    public void TooLongReason_Is_Invalid_With_Max_Length_Message()
    {
        var v = new BanPlayerRequestValidator();
        var reason = new string('x', 513);
        var result = v.TestValidate(new BanPlayerRequest(reason));
        result.ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage("Reason is too long (max 512 characters).");
    }

    [Fact]
    public void FiftyCharReason_Is_Valid()
    {
        var v = new BanPlayerRequestValidator();
        var reason = new string('x', 50);
        var result = v.TestValidate(new BanPlayerRequest(reason));
        result.ShouldNotHaveValidationErrorFor(x => x.Reason);
    }
}
