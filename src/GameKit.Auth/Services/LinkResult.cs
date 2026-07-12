// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Services;

/// <summary>Outcome discriminator of <see cref="IIdentityLinker.LinkAsync"/>.</summary>
public enum LinkResultKind
{
    /// <summary>Identity was newly linked to the caller's player.</summary>
    Linked,

    /// <summary>Identity was already linked to the caller's own player — idempotent no-op.</summary>
    AlreadyLinkedToSelf,

    /// <summary>
    /// Identity is linked to a DIFFERENT player — the endpoint layer returns HTTP 409 with
    /// <see cref="LinkResult.ExternalIdHash"/> in the body (CONTEXT D-11, D-14).
    /// </summary>
    AlreadyLinkedToOtherPlayer,
}

/// <summary>
/// The outcome of an <see cref="IIdentityLinker.LinkAsync"/> call. When <see cref="Kind"/> is
/// <see cref="LinkResultKind.AlreadyLinkedToOtherPlayer"/>, <see cref="ExternalIdHash"/> carries
/// the SHA-256 hash of the <c>(provider, external_id)</c> tuple — the 409 response body exposes
/// the hash, never the raw external id, per CONTEXT D-11 / T-02-10.
/// </summary>
/// <param name="Kind">The outcome discriminator.</param>
/// <param name="ExternalIdHash">
/// Hex-encoded SHA-256 of the colliding <c>(provider, external_id)</c>; non-null only when
/// <see cref="Kind"/> is <see cref="LinkResultKind.AlreadyLinkedToOtherPlayer"/>.
/// </param>
public sealed record LinkResult(LinkResultKind Kind, string? ExternalIdHash)
{
    /// <summary>Convenience factory for the "newly linked" outcome.</summary>
    public static LinkResult Linked() => new(LinkResultKind.Linked, null);

    /// <summary>Convenience factory for the idempotent "already linked to me" outcome.</summary>
    public static LinkResult AlreadyLinkedToSelf() => new(LinkResultKind.AlreadyLinkedToSelf, null);

    /// <summary>Convenience factory for the cross-player-collision outcome; caller supplies the hash.</summary>
    /// <param name="externalIdHash">SHA-256 of <c>"{provider}:{externalId}"</c>, hex-encoded.</param>
    public static LinkResult AlreadyLinkedToOtherPlayer(string externalIdHash) =>
        new(LinkResultKind.AlreadyLinkedToOtherPlayer, externalIdHash);
}
