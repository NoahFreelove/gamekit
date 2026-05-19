// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Cryptography;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IPartyCodeGenerator"/>. Crockford base32 alphabet (no
/// <c>I/L/O/0/1</c>; CONTEXT D-02 + RESEARCH §Don't Hand-Roll) — 30 characters; 6-char
/// codes give ~7.3·10⁸ unique combinations. Sources every character from
/// <see cref="RandomNumberGenerator"/> (CSPRNG; Pitfall §6-equivalent — never
/// <see cref="System.Random"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>RNG strategy:</b> the generator calls <see cref="RandomNumberGenerator.GetInt32(int, int)"/>
/// once per character, which the BCL implements as a rejection-sampled CSPRNG-backed
/// uniform integer. This eliminates modulo bias entirely at the cost of ~6 RNG calls per
/// code. The alternative (fill a byte buffer and modulo each byte by 30) has a small
/// modulo bias (256 % 30 = 16 → indices 0..15 are 1.04× more likely than 16..29) and was
/// rejected for v1 — the per-char path is barely more expensive and is bias-free.
/// </para>
/// <para>
/// <b>Threat reference:</b> T-05-04-04 (predictable party code via System.Random) is
/// mitigated by sourcing from CSPRNG; T-05-04-03 (low-entropy brute force) is mitigated
/// by Plan 05-08's per-IP rate limit on <c>POST /api/parties/join</c>.
/// </para>
/// </remarks>
public sealed class PartyCodeGenerator : IPartyCodeGenerator
{
    /// <summary>
    /// Crockford base32 alphabet with <c>I/L/O</c> (look-alike letters) and <c>0/1</c>
    /// (look-alike digits) removed. 30 characters.
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is outside the supported 4..16 range.</exception>
    public string GenerateCode(int length = 6)
    {
        if (length < 4 || length > 16)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Party code length must be between 4 and 16.");

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            // Per-char CSPRNG uniform pick — bias-free (RandomNumberGenerator.GetInt32 uses
            // rejection sampling internally).
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(0, Alphabet.Length)];
        }
        return new string(chars);
    }
}
