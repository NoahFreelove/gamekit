// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GameKit.Matchmaking.Services;
using Xunit;

namespace GameKit.Matchmaking.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PartyCodeGenerator"/> — Crockford base32 alphabet
/// (no <c>I/L/O/0/1</c>), CSPRNG-sourced, fixed-length codes (CONTEXT D-02).
/// </summary>
public sealed class PartyCodeGenerationTests
{
    private readonly IPartyCodeGenerator _gen = new PartyCodeGenerator();

    [Fact]
    public void GenerateCode_Default_Length_Is_Six()
    {
        var code = _gen.GenerateCode();
        Assert.Equal(6, code.Length);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(16)]
    public void GenerateCode_Honors_Length_Parameter(int length)
    {
        var code = _gen.GenerateCode(length);
        Assert.Equal(length, code.Length);
    }

    [Fact]
    public void GenerateCode_Rejects_Length_Below_Four()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _gen.GenerateCode(3));
    }

    [Fact]
    public void GenerateCode_Rejects_Length_Above_Sixteen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _gen.GenerateCode(17));
    }

    [Fact]
    public void GenerateCode_Uses_Only_Crockford_Alphabet()
    {
        // 200 codes should give us enough characters to fail loudly if any non-alphabet
        // character ever appears. The alphabet excludes I, L, O, 0, 1.
        var allowed = new HashSet<char>(PartyCodeGenerator.Alphabet);
        for (var i = 0; i < 200; i++)
        {
            var code = _gen.GenerateCode();
            foreach (var c in code)
                Assert.True(allowed.Contains(c), $"Code '{code}' contains forbidden character '{c}'.");
        }
    }

    [Fact]
    public void GenerateCode_Never_Contains_Forbidden_Characters()
    {
        // I / L / O / 0 / 1 are explicitly forbidden (Crockford base32; CONTEXT D-02
        // citing RESEARCH §Don't Hand-Roll).
        var forbidden = new[] { 'I', 'L', 'O', '0', '1' };
        for (var i = 0; i < 200; i++)
        {
            var code = _gen.GenerateCode();
            foreach (var f in forbidden)
                Assert.DoesNotContain(f.ToString(), code);
        }
    }

    [Fact]
    public void GenerateCode_Distribution_Sanity_Every_Alphabet_Char_Appears_In_1000_Codes()
    {
        // Statistical sanity check (not rigorous): with 6000 characters drawn from a
        // 30-char alphabet, every char should appear at least once. P(any specific char
        // is missing from a 6000-char sample) ≈ (29/30)^6000 ≈ 10^-89 — negligible.
        var seen = new HashSet<char>();
        for (var i = 0; i < 1000; i++)
        {
            foreach (var c in _gen.GenerateCode())
                seen.Add(c);
        }
        foreach (var c in PartyCodeGenerator.Alphabet)
            Assert.Contains(c, seen);
    }

    [Fact]
    public void GenerateCode_Two_Calls_Return_Different_Codes_With_High_Probability()
    {
        // Sanity check: 100 generations should yield 100 distinct codes (collision
        // probability is ~ C(100,2) / 30^6 ≈ 7·10^-6).
        var codes = new HashSet<string>();
        for (var i = 0; i < 100; i++)
            codes.Add(_gen.GenerateCode());

        Assert.True(codes.Count >= 99,
            $"Expected at least 99 distinct codes out of 100; got {codes.Count}.");
    }

    [Fact]
    public void Alphabet_Has_Thirty_Characters()
    {
        // Crockford base32 minus I/L/O/0/1 = 32 − 2 (the two letters that pair with 0/1
        // for confusion-free 5-bit Crockford encoding can be elided when we don't need
        // the encoding-property — and we don't, we just want unambiguous human codes).
        Assert.Equal(30, PartyCodeGenerator.Alphabet.Length);
        // Defensive: each char must be uppercase ASCII letter or digit.
        Assert.Matches("^[A-Z2-9]+$", PartyCodeGenerator.Alphabet);
    }
}
