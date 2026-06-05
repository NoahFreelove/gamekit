// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameKit.Auth.Providers.Password;

/// <summary>
/// Username + password provider (AUTH-09). Hashes via <see cref="IPasswordHasher"/> (BCrypt
/// default; Argon2 is a swappable sibling-package per AUTH-16). Runs a dummy BCrypt verify on
/// user-not-found to equalize wall-clock response time (mitigation of T-02-16, flagged as a
/// follow-up in plan 02-04's summary).
/// </summary>
/// <remarks>
/// <para><b>Contract note:</b> the <see cref="IOAuthProvider"/> shape was designed around the OAuth
/// providers (Steam/Discord), so the password-login case reuses the two string parameters:
/// <c>externalId</c> carries the <i>username</i> and <c>displayName</c> carries the <i>password</i>.
/// The <c>/auth/login/password</c> endpoint (plan 02-07) enforces this convention; callers never
/// supply these values directly. Guest providers and OAuth providers continue to use the
/// parameters per their original semantics.</para>
/// <para><b>Register path:</b> <see cref="RegisterAsync"/> is the entry point for <c>/auth/register</c>
/// when no guest JWT is presented. When a guest JWT IS presented, the endpoint calls
/// <c>IGuestUpgradeService.UpgradeToPasswordAsync</c> instead (CONTEXT D-12).</para>
/// </remarks>
internal sealed class PasswordOAuthProvider : IOAuthProvider
{
    // Stable hash used to equalize timing when username lookup misses (T-02-16 mitigation).
    // BCrypt.Verify runs its full work-factor-12 Blowfish key-setup only when the hash is
    // a syntactically-valid 60-character BCrypt string. A malformed hash causes BCrypt.Net-Next
    // to throw SaltParseException immediately — before any crypto work — creating a timing oracle
    // (pre-existing defect since 02-03, surfaced by Phase 7 review, CR-01).
    // Generated via: BCrypt.Net.BCrypt.HashPassword("gamekit-dummy-password-never-matches-7f3k9m", 12)
    // Verified: DummyHash.Length == 60 and BCrypt.Net.BCrypt.Verify("x", DummyHash) == false (no exception).
    // Keep DISTINCT from AdminAuthService.DummyHash (different password input).
    private const string DummyHash = "$2a$12$m90Ov/cRI/Zgu5myAT5uWu1ZEfnp9lsBmeA6FUoH.LtS7GfjQcLmK";

    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenService _refresh;
    private readonly IAuthAuditWriter _audit;

    /// <summary>Constructs the provider.</summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">UUIDv7 id generator.</param>
    /// <param name="hasher">Password hasher (BCrypt by default).</param>
    /// <param name="refresh">Refresh-token service that issues the root token on success.</param>
    /// <param name="audit">Audit writer for login-failure + credential-set rows.</param>
    public PasswordOAuthProvider(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator ids,
        IPasswordHasher hasher,
        IRefreshTokenService refresh,
        IAuthAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(audit);
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
        _hasher = hasher;
        _refresh = refresh;
        _audit = audit;
    }

    /// <inheritdoc />
    public string Provider => "password";

    /// <summary>
    /// Login path. <paramref name="externalId"/> carries the username; <paramref name="displayName"/>
    /// carries the password (endpoint-layer convention, see remarks on the class).
    /// <paramref name="avatarUrl"/> is ignored.
    /// </summary>
    /// <inheritdoc />
    public async Task<OAuthResult> CompleteLoginAsync(
        string externalId,
        string? displayName,
        string? avatarUrl,
        string? fingerprint,
        CancellationToken cancellationToken = default)
    {
        _ = avatarUrl;

        var username = externalId;
        var password = displayName ?? string.Empty;

        var credential = await _ctx.Set<PlayerCredential>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Username == username, cancellationToken)
            .ConfigureAwait(false);

        if (credential is null)
        {
            // T-02-16 timing-attack mitigation: run BCrypt.Verify against a known-bad hash so wall-clock
            // cost is parity with the hit path. Return value is discarded.
            _ = _hasher.Verify(password, DummyHash);
            await _audit.WriteAsync(
                action: "auth.login.failure",
                targetType: "player",
                targetId: null,
                actorId: null,
                after: new { provider = "password", reason_code = "unknown_username" },
                reason: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return OAuthResult.Fail("invalid_credentials");
        }

        if (!_hasher.Verify(password, credential.PasswordHash))
        {
            await _audit.WriteAsync(
                action: "auth.login.failure",
                targetType: "player",
                targetId: credential.PlayerId,
                actorId: credential.PlayerId,
                after: new { provider = "password", reason_code = "wrong_password" },
                reason: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return OAuthResult.Fail("invalid_credentials");
        }

        // AUTH-18 rehash-on-verify: when Argon2idPasswordHasher is the active IPasswordHasher,
        // NeedsRehash returns true for $2a$/$2b$ prefixes so BCrypt hashes are transparently
        // upgraded to Argon2id in the same request scope (RESEARCH §Pitfall 3 — the UPDATE must
        // commit here or the column never migrates). BCryptPasswordHasher.NeedsRehash always
        // returns false so this block is a no-op under the default hasher.
        // The block is placed AFTER the successful Verify and BEFORE BannedCheckHelper to ensure
        // it never executes on the user-not-found dummy-hash path (T-07-06-02).
        if (_hasher.NeedsRehash(credential.PasswordHash))
        {
            // Re-load as a tracked entity for the UPDATE — the original `credential` was
            // fetched with AsNoTracking() and cannot be used for change-tracking writes.
            // The filter uses PlayerId (PK) to prevent accidentally updating another player's
            // credential row (T-07-06-03).
            var tracked = await _ctx.Set<PlayerCredential>()
                .FirstAsync(c => c.PlayerId == credential.PlayerId, cancellationToken)
                .ConfigureAwait(false);
            tracked.PasswordHash = _hasher.Hash(password);
            tracked.UpdatedAt = _clock.UtcNow;
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var banned = await BannedCheckHelper.CheckAsync(_ctx, credential.PlayerId, cancellationToken).ConfigureAwait(false);
        if (banned is not null) return banned;
        var tokens = await _refresh
            .IssueRootAsync(credential.PlayerId, Provider, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        return OAuthResult.Ok(credential.PlayerId, tokens);
    }

    /// <summary>
    /// Registers a new player with a username + password credential. Called from
    /// <c>/auth/register</c> when no guest JWT is present (the upgrade-in-place path is handled by
    /// <c>IGuestUpgradeService.UpgradeToPasswordAsync</c> per CONTEXT D-12).
    /// </summary>
    /// <param name="username">The desired username; subject to the CITEXT-shaped UNIQUE index.</param>
    /// <param name="password">The plaintext password; hashed via <see cref="IPasswordHasher"/>.</param>
    /// <param name="displayName">Optional display name; falls back to <paramref name="username"/>.</param>
    /// <param name="fingerprint">Optional client device fingerprint for the issued refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="OAuthResult.Ok"/> on success; <see cref="OAuthResult.Fail"/> with
    /// <c>username_taken</c> when a concurrent register won the UNIQUE(Username) race. The
    /// endpoint layer translates the failure to HTTP 409 (RESEARCH §15 open question #3).
    /// </returns>
    public async Task<OAuthResult> RegisterAsync(
        string username,
        string password,
        string? displayName,
        string? fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var playerId = _ids.NewId();
        var display = string.IsNullOrWhiteSpace(displayName) ? username : displayName!;

        _ctx.Players.Add(new Player
        {
            Id = playerId,
            DisplayName = display,
            CreatedAt = _clock.UtcNow,
        });
        _ctx.Set<PlayerCredential>().Add(new PlayerCredential
        {
            PlayerId = playerId,
            Username = username,
            PasswordHash = _hasher.Hash(password),
            UpdatedAt = _clock.UtcNow,
        });

        try
        {
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (TryFindPostgresException(ex) is { SqlState: "23505" })
        {
            // UNIQUE(Username) collision — a concurrent register won. Detach the in-flight entities
            // so the scoped DbContext remains usable (e.g., for the audit-row write that follows).
            // Catch broader than DbUpdateException because Npgsql's default execution strategy
            // wraps transient failures (40001) in InvalidOperationException; the 23505 path can
            // also surface this way when surrounded by SERIALIZABLE-aware callers.
            foreach (var entry in _ctx.ChangeTracker.Entries())
            {
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }

            await _audit.WriteAsync(
                action: "auth.register.failure",
                targetType: "player",
                targetId: null,
                actorId: null,
                after: new { provider = "password", reason_code = "username_taken" },
                reason: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return OAuthResult.Fail("username_taken");
        }

        await _audit.WriteAsync(
            action: "auth.credential.password_set",
            targetType: "player_credential",
            targetId: playerId,
            actorId: playerId,
            after: new { password_set = true },
            reason: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // D-03 ban enforcement: a freshly-registered player is never banned, but invoking
        // the shared helper keeps every provider code path consistent and defends against
        // future refactors that might reuse Player rows across registrations.
        var banned = await BannedCheckHelper.CheckAsync(_ctx, playerId, cancellationToken).ConfigureAwait(false);
        if (banned is not null) return banned;
        var tokens = await _refresh
            .IssueRootAsync(playerId, Provider, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        return OAuthResult.Ok(playerId, tokens);
    }

    /// <summary>
    /// Walks an exception's InnerException chain (bounded depth) looking for a
    /// <see cref="PostgresException"/>. Required because Npgsql's default execution strategy
    /// may wrap provider exceptions in <see cref="InvalidOperationException"/>, and EF further
    /// wraps them in <see cref="DbUpdateException"/>.
    /// </summary>
    private static PostgresException? TryFindPostgresException(Exception? ex)
    {
        for (var i = 0; i < 8 && ex is not null; i++)
        {
            if (ex is PostgresException pg) return pg;
            ex = ex.InnerException;
        }
        return null;
    }
}
