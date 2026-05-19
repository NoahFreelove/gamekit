// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IPartyService"/>. SERIALIZABLE-transaction-driven party CRUD against
/// the <see cref="Party"/> + <see cref="PartyMember"/> entities (Plan 05-02). Closes
/// MATCH-03 (party_members 1-N) at the application-service level.
/// </summary>
/// <remarks>
/// <para>
/// <b>SERIALIZABLE enforcement (RESEARCH §OQ-2-RESOLVED):</b> every mutating operation
/// runs under <see cref="IsolationLevel.Serializable"/>. Postgres MAY raise 40001
/// serialization_failure at commit time when two transactions touch the same player's
/// active-membership set; the Polly retry pipeline in
/// <see cref="SerializationFailureRetry"/> retries up to 3 times with exponential
/// backoff + jitter (CR-03 mirror).
/// </para>
/// <para>
/// <b>UNIQUE-violation retry for code generation:</b> the party_code UNIQUE column has a
/// finite collision space (30⁶ ≈ 7.3·10⁸ for 6-char codes). When INSERT fails with
/// <c>23505</c> on the code unique, <see cref="CreateAsync"/> regenerates and retries up to
/// 5 times before throwing <see cref="PartyConflictException"/> with code
/// <c>party_code_exhausted</c>.
/// </para>
/// <para>
/// <b>Citext-aware lookup (Pitfall §9):</b> the SQL <c>WHERE party_code = @code</c> is
/// case-insensitive because <c>party_code</c> is declared <c>citext</c> in the Plan 05-02
/// migration. The service does NOT call <see cref="string.ToUpperInvariant"/> on the
/// incoming code.
/// </para>
/// </remarks>
public sealed class PartyService : IPartyService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IPartyCodeGenerator _codes;
    private readonly ResiliencePipeline _serializationRetry;

    /// <summary>States considered "active" for single-active-party enforcement (CONTEXT D-02).</summary>
    private static readonly PartyState[] ActiveStates =
        { PartyState.Open, PartyState.Queueing, PartyState.InMatch };

    /// <summary>Maximum unique-code-collision retries before <see cref="CreateAsync"/> gives up.</summary>
    private const int MaxCodeCollisionRetries = 5;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">Id generator (UUIDv7).</param>
    /// <param name="codes">Party-code generator.</param>
    /// <param name="logger">Logger for serialization-failure retry diagnostics.</param>
    public PartyService(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator ids,
        IPartyCodeGenerator codes,
        ILogger<PartyService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(codes);
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
        _codes = codes;
        _serializationRetry = SerializationFailureRetry.Build(logger, nameof(PartyService));
    }

    /// <inheritdoc />
    public async Task<Party> CreateAsync(Guid ownerPlayerId, CancellationToken ct = default)
    {
        return await _serializationRetry.ExecuteAsync(async cancellationToken =>
            await CreateCoreAsync(ownerPlayerId, cancellationToken).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    private async Task<Party> CreateCoreAsync(Guid ownerPlayerId, CancellationToken ct)
    {
        // The unique-code-collision retry loop drives a fresh SERIALIZABLE transaction
        // per attempt. We cap at MaxCodeCollisionRetries; on the final attempt's failure
        // we throw party_code_exhausted.
        for (var attempt = 1; attempt <= MaxCodeCollisionRetries; attempt++)
        {
            // EF must clear tracked state between attempts — otherwise a failed attempt's
            // entity instances pollute the next attempt.
            _ctx.ChangeTracker.Clear();

            await using var tx = await _ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct)
                .ConfigureAwait(false);

            // Active-membership check. Joins party_members → parties and filters on
            // state ∈ ActiveStates. SERIALIZABLE serializes this read against concurrent
            // inserts in JoinAsync / CreateAsync.
            await GuardNoActiveMembershipAsync(ownerPlayerId, ct).ConfigureAwait(false);

            var code = _codes.GenerateCode();
            var now = _clock.UtcNow;
            var party = new Party
            {
                Id = _ids.NewId(),
                PartyCode = code,
                State = PartyState.Open,
                OwnerPlayerId = ownerPlayerId,
                CreatedAt = now,
                ExpiresAt = null,
            };
            var member = new PartyMember
            {
                Id = _ids.NewId(),
                PartyId = party.Id,
                PlayerId = ownerPlayerId,
                JoinedAt = now,
            };

            _ctx.Set<Party>().Add(party);
            _ctx.Set<PartyMember>().Add(member);

            try
            {
                await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return party;
            }
            catch (DbUpdateException ex) when (IsCodeUniqueViolation(ex))
            {
                // Code collision — roll back, clear tracker, and try a fresh code on the
                // next loop iteration. The current transaction is poisoned by the failed
                // SaveChanges and cannot be reused.
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                _ctx.ChangeTracker.Clear();
                if (attempt == MaxCodeCollisionRetries)
                {
                    throw new PartyConflictException(
                        "party_code_exhausted",
                        $"Failed to generate a unique party code after {MaxCodeCollisionRetries} attempts.");
                }
            }
        }

        // Unreachable — the loop either returns or throws at the final attempt.
        throw new InvalidOperationException("Unreachable: CreateAsync retry loop exited without return or throw.");
    }

    /// <inheritdoc />
    public async Task<Party> JoinAsync(string code, Guid playerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return await _serializationRetry.ExecuteAsync(async cancellationToken =>
            await JoinCoreAsync(code, playerId, cancellationToken).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    private async Task<Party> JoinCoreAsync(string code, Guid playerId, CancellationToken ct)
    {
        _ctx.ChangeTracker.Clear();

        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        // 1. Lookup by citext party_code (case-insensitive at the SQL level — Pitfall §9).
        //    No ToUpperInvariant call; the column type does the work.
        var party = await _ctx.Set<Party>()
            .FirstOrDefaultAsync(p => p.PartyCode == code, ct)
            .ConfigureAwait(false)
            ?? throw new PartyInvalidStateException(
                "party_not_found",
                $"No party with code '{code}'.");

        // 2. State guard.
        if (party.State != PartyState.Open)
            throw new PartyInvalidStateException(
                "party_not_open",
                $"Party {party.Id} is in state {party.State}, not Open.");

        // 3. Active-membership guard for the joining player.
        await GuardNoActiveMembershipAsync(playerId, ct).ConfigureAwait(false);

        // 4. Idempotency: if the player is already a member of THIS party (composite
        //    UNIQUE handles the at-row-level concurrent insert), return the party as-is.
        var alreadyMember = await _ctx.Set<PartyMember>()
            .AnyAsync(m => m.PartyId == party.Id && m.PlayerId == playerId, ct)
            .ConfigureAwait(false);
        if (alreadyMember)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return party;
        }

        // 5. Insert membership row.
        _ctx.Set<PartyMember>().Add(new PartyMember
        {
            Id = _ids.NewId(),
            PartyId = party.Id,
            PlayerId = playerId,
            JoinedAt = _clock.UtcNow,
        });

        try
        {
            await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return party;
        }
        catch (DbUpdateException ex) when (IsMemberUniqueViolation(ex))
        {
            // Race: another caller inserted the same (PartyId, PlayerId) row between the
            // AnyAsync check and the INSERT. Treat as success (idempotent join).
            _ctx.ChangeTracker.Clear();
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return party;
        }
    }

    /// <inheritdoc />
    public async Task DissolveAsync(Guid partyId, Guid actorPlayerId, CancellationToken ct = default)
    {
        await _serializationRetry.ExecuteAsync(async cancellationToken =>
        {
            await DissolveCoreAsync(partyId, actorPlayerId, cancellationToken).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task DissolveCoreAsync(Guid partyId, Guid actorPlayerId, CancellationToken ct)
    {
        _ctx.ChangeTracker.Clear();

        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        var party = await _ctx.Set<Party>()
            .FirstOrDefaultAsync(p => p.Id == partyId, ct)
            .ConfigureAwait(false)
            ?? throw new PartyInvalidStateException(
                "party_not_found",
                $"Party {partyId} does not exist.");

        if (party.State == PartyState.Dissolved)
            throw new PartyInvalidStateException(
                "party_already_dissolved",
                $"Party {partyId} is already dissolved.");

        if (party.OwnerPlayerId != actorPlayerId)
            throw new PartyAuthorizationException(
                "not_party_owner",
                $"Player {actorPlayerId} is not the owner of party {partyId}.");

        party.State = PartyState.Dissolved;
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Party?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return await _ctx.Set<Party>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartyCode == code, ct)
            .ConfigureAwait(false);
    }

    private async Task GuardNoActiveMembershipAsync(Guid playerId, CancellationToken ct)
    {
        // EF translates this to a JOIN with WHERE on the active-state set. The
        // ActiveStates array is hoisted to a constant array; EF parameterizes it via ANY().
        var alreadyActive = await _ctx.Set<PartyMember>()
            .Join(
                _ctx.Set<Party>(),
                m => m.PartyId,
                p => p.Id,
                (m, p) => new { m.PlayerId, p.State })
            .AnyAsync(x => x.PlayerId == playerId && ActiveStates.Contains(x.State), ct)
            .ConfigureAwait(false);

        if (alreadyActive)
            throw new PartyConflictException(
                "player_already_in_party",
                $"Player {playerId} is already a member of an active party (state ∈ {{ Open, Queueing, InMatch }}).");
    }

    private static bool IsCodeUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg
            && pg.SqlState == "23505"
            && pg.ConstraintName is string c
            && c.Contains("party_code", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMemberUniqueViolation(DbUpdateException ex)
    {
        // Composite UNIQUE on (PartyId, PlayerId) — see Plan 05-02 PartyMemberConfiguration.
        return ex.InnerException is PostgresException pg
            && pg.SqlState == "23505"
            && pg.ConstraintName is string c
            && (c.Contains("party_member", StringComparison.OrdinalIgnoreCase)
                || c.Contains("PartyId_PlayerId", StringComparison.OrdinalIgnoreCase));
    }
}
