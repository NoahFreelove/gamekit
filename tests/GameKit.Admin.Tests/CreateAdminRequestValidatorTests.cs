// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation.TestHelper;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Admin.UI.Http.Validators;
using Xunit;

namespace GameKit.Admin.Tests;

/// <summary>
/// Unit tests for <see cref="CreateAdminRequestValidator"/>. Verifies the username regex
/// (<c>^[a-z0-9_-]{3,32}$</c>), role enum (admin/superadmin), and min-length password checks.
/// </summary>
public sealed class CreateAdminRequestValidatorTests
{
    [Theory]
    [InlineData("AB")]          // too short
    [InlineData("Admin")]       // uppercase disallowed
    [InlineData("has space")]   // space disallowed
    [InlineData("super!")]      // punctuation other than _- disallowed
    public void InvalidUsername_Fails_Regex(string username)
    {
        var v = new CreateAdminRequestValidator();
        var result = v.TestValidate(new CreateAdminRequest(username, "hunter2hunter2", "admin"));
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Theory]
    [InlineData("root")]            // valid lowercase
    [InlineData("root_01")]         // underscore + digit
    [InlineData("root-admin")]      // hyphen
    [InlineData("abc")]             // min length 3
    public void ValidUsername_Passes_Regex(string username)
    {
        var v = new CreateAdminRequestValidator();
        var result = v.TestValidate(new CreateAdminRequest(username, "hunter2hunter2", "admin"));
        result.ShouldNotHaveValidationErrorFor(x => x.Username);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("ADMIN")]
    [InlineData("")]
    public void InvalidRole_Fails(string role)
    {
        var v = new CreateAdminRequestValidator();
        var result = v.TestValidate(new CreateAdminRequest("root", "hunter2hunter2", role));
        result.ShouldHaveValidationErrorFor(x => x.Role);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superadmin")]
    public void ValidRole_Passes(string role)
    {
        var v = new CreateAdminRequestValidator();
        var result = v.TestValidate(new CreateAdminRequest("root", "hunter2hunter2", role));
        result.ShouldNotHaveValidationErrorFor(x => x.Role);
    }

    [Fact]
    public void ShortPassword_Fails_Min_Length()
    {
        var v = new CreateAdminRequestValidator();
        var result = v.TestValidate(new CreateAdminRequest("root", "short", "admin"));
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void EightCharPassword_Passes()
    {
        var v = new CreateAdminRequestValidator();
        var result = v.TestValidate(new CreateAdminRequest("root", "12345678", "admin"));
        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }
}
