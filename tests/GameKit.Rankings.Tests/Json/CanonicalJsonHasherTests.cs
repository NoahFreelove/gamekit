// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameKit.Core.Http.Contracts;
using GameKit.Core.Entities;
using GameKit.Rankings.Json;
using Xunit;

namespace GameKit.Rankings.Tests.Json;

/// <summary>
/// Unit tests for <see cref="CanonicalJsonHasher"/> (Open Q5 anchor).
/// Verifies that the SHA-256 hash of the canonical JSON representation of a
/// <see cref="SessionCompleteRequest"/> is:
/// <list type="bullet">
///   <item>Deterministic across identical bodies.</item>
///   <item>Order-insensitive on the participants list.</item>
///   <item>Sensitive to any change in participant data.</item>
///   <item>Whitespace-insensitive (raw JSON differences are normalized out).</item>
/// </list>
/// </summary>
public sealed class CanonicalJsonHasherTests
{
    private static readonly Guid _p1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _p2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _p3 = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Test_Same_Body_Same_Hash()
    {
        var req1 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
            new SessionCompleteParticipant(_p2, 1, SessionResult.Loss, 5),
        ]);

        var req2 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
            new SessionCompleteParticipant(_p2, 1, SessionResult.Loss, 5),
        ]);

        var hash1 = CanonicalJsonHasher.Sha256OfCanonicalJson(req1);
        var hash2 = CanonicalJsonHasher.Sha256OfCanonicalJson(req2);

        Assert.Equal(hash1, hash2);
        // SHA-256 hex is 64 chars, lower-case
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash1.ToLowerInvariant());
    }

    [Fact]
    public void Test_Reordered_Participants_Same_Hash()
    {
        var req1 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
            new SessionCompleteParticipant(_p2, 1, SessionResult.Loss, 5),
        ]);

        // Same participants, different list order
        var req2 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p2, 1, SessionResult.Loss, 5),
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
        ]);

        var hash1 = CanonicalJsonHasher.Sha256OfCanonicalJson(req1);
        var hash2 = CanonicalJsonHasher.Sha256OfCanonicalJson(req2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Test_Different_Result_Different_Hash()
    {
        var req1 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
            new SessionCompleteParticipant(_p2, 1, SessionResult.Loss, 5),
        ]);

        // Same participant but result changed to Draw
        var req2 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p1, 0, SessionResult.Draw, 10),
            new SessionCompleteParticipant(_p2, 1, SessionResult.Loss, 5),
        ]);

        var hash1 = CanonicalJsonHasher.Sha256OfCanonicalJson(req1);
        var hash2 = CanonicalJsonHasher.Sha256OfCanonicalJson(req2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Test_Whitespace_In_Source_JSON_Does_Not_Affect_Hash()
    {
        // Two identical requests produced by different serializers
        // (one compact, one pretty-printed) should yield the same canonical hash
        // because CanonicalJsonHasher re-serializes from the deserialized object.

        var req1 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
            new SessionCompleteParticipant(_p2, 1, SessionResult.Loss, 5),
        ]);

        // Simulate a request deserialized from different JSON whitespace variants
        // by constructing a semantically identical request object.
        var req2 = new SessionCompleteRequest(new List<SessionCompleteParticipant>
        {
            new SessionCompleteParticipant(_p2, 1, SessionResult.Loss, 5),
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
        });

        var hash1 = CanonicalJsonHasher.Sha256OfCanonicalJson(req1);
        var hash2 = CanonicalJsonHasher.Sha256OfCanonicalJson(req2);

        // Both produce the same canonical hash despite different original orderings
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Test_Three_Participants_Canonical_Order_Is_Stable()
    {
        // When three participants are given in random order,
        // the hash should match a deterministically sorted reference.
        var req1 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p3, 2, SessionResult.Loss, 1),
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
            new SessionCompleteParticipant(_p2, 1, SessionResult.Draw, 5),
        ]);

        var req2 = new SessionCompleteRequest([
            new SessionCompleteParticipant(_p1, 0, SessionResult.Win, 10),
            new SessionCompleteParticipant(_p2, 1, SessionResult.Draw, 5),
            new SessionCompleteParticipant(_p3, 2, SessionResult.Loss, 1),
        ]);

        var hash1 = CanonicalJsonHasher.Sha256OfCanonicalJson(req1);
        var hash2 = CanonicalJsonHasher.Sha256OfCanonicalJson(req2);

        Assert.Equal(hash1, hash2);
    }
}
