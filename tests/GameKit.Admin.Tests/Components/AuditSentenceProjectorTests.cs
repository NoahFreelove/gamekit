// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Admin.UI.Http.Contracts;
using GameKit.Admin.UI.Services;
using Xunit;

namespace GameKit.Admin.Tests.Components;

/// <summary>
/// Phase 03.1 D-12 / D-14 — verifies all 7 known AdminAuditActions namespaces produce the
/// expected SentenceModel; unknown actions fall through to the D-14 generic fallback.
/// </summary>
public sealed class AuditSentenceProjectorTests
{
    [Theory]
    [Trait("Category", "Component")]
    [InlineData("admin.player.ban", "alice", "bob", "banned", "bob", "spam")]
    [InlineData("admin.player.unban", "alice", "bob", "unbanned", "bob", null)]
    [InlineData("admin.player.gdpr_delete", "alice", "bob", "GDPR-deleted", "bob", "gdpr_request")]
    [InlineData("admin.player.rank_adjust", "alice", "bob", "adjusted rank for", "bob", "manual_correction")]
    [InlineData("admin.admin.create", "alice", "carol", "created admin", "carol", null)]
    [InlineData("admin.admin.delete", "alice", "carol", "deleted admin", "carol", null)]
    [InlineData("admin.signing_key.rotate", "alice", null, "rotated JWT signing key", "current key", "scheduled rotation")]
    public void Render_KnownAction_ProducesExpectedSentence(
        string action,
        string actor,
        string? target,
        string expectedIntro,
        string expectedTarget,
        string? expectedReason)
    {
        var result = AuditSentenceTemplates.Render(
            new SentenceContext(action, actor, target, null, null, expectedReason));

        Assert.Equal(actor, result.Actor);
        Assert.Equal(expectedIntro, result.Intro);
        Assert.Equal(expectedTarget, result.Target);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void Render_UnknownAction_ReturnsFallbackWithSpacedAction()
    {
        var result = AuditSentenceTemplates.Render(
            new SentenceContext("admin.future.thing", "alice", "bob", null, null, "some reason"));

        Assert.Equal("alice", result.Actor);
        Assert.Equal("performed", result.Intro);
        Assert.Equal("admin future thing", result.Target);
        Assert.Equal("on bob", result.Modifier);
        Assert.Equal("some reason", result.Reason);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void Render_UnknownAction_NullTarget_ProducesFallbackWithoutModifier()
    {
        var result = AuditSentenceTemplates.Render(
            new SentenceContext("admin.future.thing", "alice", null, null, null, null));

        Assert.Equal("performed", result.Intro);
        Assert.Equal("admin future thing", result.Target);
        Assert.Null(result.Modifier);
    }
}
