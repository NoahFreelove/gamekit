// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Lobby.Hubs;
using GameKit.Matchmaking.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LobbyEntity = GameKit.Lobby.Entities.Lobby;
using LobbyMemberEntity = GameKit.Lobby.Entities.LobbyMember;
using LobbyState = GameKit.Lobby.Entities.LobbyState;
using MatchmakingParty = GameKit.Matchmaking.Entities.Party;

// Cross-package link: TryStartMatchmakingAsync creates a Matchmaking Party via IPartyService and
// calls IMatchmakingService.EnqueueAsync(partyId) — the Party row is the intentional cross-package
// link. No lobby_id FK is added to matchmaking_tickets (migration boundary, LOBBY-05 deviation Q1).

namespace GameKit.Lobby.Services;

/// <summary>
/// Default implementation of <see cref="ILobbyService"/>. Provides lobby CRUD, member
/// management, a SERIALIZABLE all-ready state machine, and real matchmaking submission via
/// <see cref="IPartyService"/> + <see cref="IMatchmakingService"/> (LOBBY-02, LOBBY-03, LOBBY-05).
/// </summary>
/// <remarks>
/// Registered as <c>Scoped</c> — shares the ambient <see cref="GameKitDbContext"/> lifetime
/// with the calling endpoint or hub context.
/// </remarks>
internal sealed class LobbyService : ILobbyService
{
    private readonly GameKitDbContext _ctx;
    private readonly IHubContext<LobbyHub, ILobbyClient> _hubContext;
    private readonly ILogger<LobbyService> _logger;
    private readonly GameKitLobbyOptions _options;
    private readonly IIdGenerator _ids;
    private readonly IPartyService _partyService;
    private readonly IMatchmakingService _matchmakingService;

    /// <summary>Constructs the service.</summary>
    public LobbyService(
        GameKitDbContext ctx,
        IHubContext<LobbyHub, ILobbyClient> hubContext,
        ILogger<LobbyService> logger,
        IOptions<GameKitLobbyOptions> options,
        IIdGenerator ids,
        IPartyService partyService,
        IMatchmakingService matchmakingService)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(hubContext);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(partyService);
        ArgumentNullException.ThrowIfNull(matchmakingService);
        _ctx = ctx;
        _hubContext = hubContext;
        _logger = logger;
        _options = options.Value;
        _ids = ids;
        _partyService = partyService;
        _matchmakingService = matchmakingService;
    }

    /// <inheritdoc />
    public async Task<LobbyEntity> CreateLobbyAsync(
        Guid ownerId,
        int? maxMembers = null,
        Guid? ladderId = null,
        string? regionName = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var lobby = new LobbyEntity
        {
            Id = _ids.NewId(),
            OwnerId = ownerId,
            LadderId = ladderId,
            State = LobbyState.Open,
            MaxMembers = maxMembers ?? _options.DefaultMaxMembers,
            RegionName = regionName,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var member = new LobbyMemberEntity
        {
            Id = _ids.NewId(),
            LobbyId = lobby.Id,
            PlayerId = ownerId,
            Ready = false,
            JoinedAt = now,
        };

        _ctx.Set<LobbyEntity>().Add(lobby);
        _ctx.Set<LobbyMemberEntity>().Add(member);
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        lobby.Members.Add(member);
        return lobby;
    }

    /// <inheritdoc />
    public async Task<LobbyEntity> JoinLobbyAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default)
    {
        var lobby = await _ctx.Set<LobbyEntity>()
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == lobbyId, ct)
            .ConfigureAwait(false)
            ?? throw new LobbyNotFoundException(lobbyId);

        if (lobby.Members.Count >= lobby.MaxMembers)
            throw new LobbyFullException(lobbyId, lobby.MaxMembers);

        if (lobby.Members.Any(m => m.PlayerId == playerId))
            throw new AlreadyMemberException(lobbyId, playerId);

        var member = new LobbyMemberEntity
        {
            Id = _ids.NewId(),
            LobbyId = lobbyId,
            PlayerId = playerId,
            Ready = false,
            JoinedAt = DateTimeOffset.UtcNow,
        };

        _ctx.Set<LobbyMemberEntity>().Add(member);
        lobby.UpdatedAt = DateTimeOffset.UtcNow;
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        lobby.Members.Add(member);
        return lobby;
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(
        Guid lobbyId,
        Guid actorId,
        Guid targetPlayerId,
        CancellationToken ct = default)
    {
        var lobby = await _ctx.Set<LobbyEntity>()
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == lobbyId, ct)
            .ConfigureAwait(false)
            ?? throw new LobbyNotFoundException(lobbyId);

        // Owner-or-self authorization.
        if (actorId != lobby.OwnerId && actorId != targetPlayerId)
            throw new LobbyAuthorizationException(lobbyId, actorId);

        var member = lobby.Members.FirstOrDefault(m => m.PlayerId == targetPlayerId)
            ?? throw new NotAMemberException(lobbyId, targetPlayerId);

        _ctx.Set<LobbyMemberEntity>().Remove(member);
        lobby.UpdatedAt = DateTimeOffset.UtcNow;
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> IsMemberAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default)
        => _ctx.Set<LobbyMemberEntity>()
            .AnyAsync(m => m.LobbyId == lobbyId && m.PlayerId == playerId, ct);

    /// <inheritdoc />
    public async Task MarkReadyAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default)
    {
        // Phase 1: SERIALIZABLE transaction — marks the member ready and, when all members
        // are ready, transitions the lobby to InGame as an atomic gate.
        //
        // IMPORTANT — cross-service transaction boundary:
        // IPartyService.CreateAsync (called in TryStartMatchmakingAsync) opens its own
        // SERIALIZABLE transaction on the same GameKitDbContext. EF Core does not support
        // nested transactions, so TryStartMatchmakingAsync MUST run AFTER the lobby tx
        // commits. The InGame state is set inside the lobby tx to prevent a second concurrent
        // MarkReady from also entering the all-ready branch (double-submission guard).
        var pipeline = SerializationFailureRetry.Build(_logger, "LobbyMarkReady");

        LobbyState stateAfterCommit = default;
        IReadOnlyList<LobbyMemberEntity> membersSnapshot = Array.Empty<LobbyMemberEntity>();
        Guid? ownerIdSnapshot = null;
        bool allReadyTriggered = false;

        await pipeline.ExecuteAsync(async innerCt =>
        {
            // Clear EF tracker on each retry attempt so stale tracked entities don't
            // interfere with reloads (mirrors PartyService.CreateCoreAsync pattern).
            _ctx.ChangeTracker.Clear();

            await using var tx = await _ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, innerCt)
                .ConfigureAwait(false);

            var lobby = await _ctx.Set<LobbyEntity>()
                .Include(l => l.Members)
                .FirstOrDefaultAsync(l => l.Id == lobbyId, innerCt)
                .ConfigureAwait(false)
                ?? throw new LobbyNotFoundException(lobbyId);

            var member = lobby.Members.FirstOrDefault(m => m.PlayerId == playerId)
                ?? throw new NotAMemberException(lobbyId, playerId);

            member.Ready = true;
            lobby.UpdatedAt = DateTimeOffset.UtcNow;

            // All-ready gate — set InGame optimistically inside the tx to prevent a second
            // concurrent MarkReady from also seeing the all-ready condition (State guard).
            // TryStartMatchmakingAsync runs after this tx commits.
            allReadyTriggered = lobby.Members.All(m => m.Ready)
                && lobby.State == LobbyState.ReadyChecking;

            if (allReadyTriggered)
            {
                lobby.State = LobbyState.InGame;
            }

            await _ctx.SaveChangesAsync(innerCt).ConfigureAwait(false);
            await tx.CommitAsync(innerCt).ConfigureAwait(false);

            stateAfterCommit = lobby.State;
            membersSnapshot = lobby.Members.ToList();
            ownerIdSnapshot = lobby.OwnerId;
        }, ct).ConfigureAwait(false);

        // Phase 2: real matchmaking submission — runs OUTSIDE the lobby tx.
        // IPartyService.CreateAsync and IMatchmakingService.EnqueueAsync each open their
        // own SERIALIZABLE transactions on _ctx; they cannot be nested inside the lobby tx.
        if (allReadyTriggered)
        {
            stateAfterCommit = await TryStartMatchmakingAsync(
                lobbyId, ownerIdSnapshot, membersSnapshot, ct)
                .ConfigureAwait(false);
        }

        // Broadcast AFTER commit — IHubContext is not transactional (T-11-03-06).
        await _hubContext.Clients
            .Group($"lobby:{lobbyId}")
            .ReceiveStateUpdateAsync(new LobbyStateUpdate(lobbyId, stateAfterCommit))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<LobbyEntity?> GetLobbyAsync(Guid lobbyId, CancellationToken ct = default)
        => _ctx.Set<LobbyEntity>()
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == lobbyId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetPlayerLobbyIdsAsync(
        Guid playerId,
        CancellationToken ct = default)
    {
        return await _ctx.Set<LobbyMemberEntity>()
            .Where(m => m.PlayerId == playerId)
            .Select(m => m.LobbyId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    // ---- private helpers ----

    /// <summary>
    /// Submits the lobby to matchmaking by creating a Matchmaking Party for all members,
    /// calling <see cref="IMatchmakingService.EnqueueAsync"/>, and returning the final
    /// <see cref="LobbyState"/> (<c>InGame</c> on success, <c>ReadyChecking</c> on rejection).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cross-package link:</b> this method creates a <c>Party</c> row via
    /// <see cref="IPartyService"/> and passes the <c>party.Id</c> to
    /// <see cref="IMatchmakingService.EnqueueAsync"/>. The Party row is the intentional
    /// cross-package link between Lobby and Matchmaking — <b>no <c>lobby_id</c> FK is added to
    /// <c>matchmaking_tickets</c></b> (migration boundary, LOBBY-05 deviation documented in
    /// <c>11-RESEARCH.md §Open Questions Q1</c>).
    /// </para>
    /// <para>
    /// <b>Transaction boundary:</b> this method runs OUTSIDE the lobby SERIALIZABLE tx.
    /// <see cref="IPartyService.CreateAsync"/> opens its own SERIALIZABLE transaction on the
    /// shared <see cref="GameKitDbContext"/> — EF Core does not support nested transactions.
    /// The lobby tx sets <c>State = InGame</c> optimistically before committing; if the
    /// matchmaking submission is rejected, this method reverts the state back to
    /// <c>ReadyChecking</c> in a new (non-SERIALIZABLE) transaction.
    /// </para>
    /// </remarks>
    /// <param name="lobbyId">Lobby id (for the revert transaction and logging).</param>
    /// <param name="ownerIdNullable">Owner player id (nullable — checked before submission).</param>
    /// <param name="members">Member snapshot captured inside the lobby tx.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final <see cref="LobbyState"/> — <c>InGame</c> or <c>ReadyChecking</c>.</returns>
    private async Task<LobbyState> TryStartMatchmakingAsync(
        Guid lobbyId,
        Guid? ownerIdNullable,
        IReadOnlyList<LobbyMemberEntity> members,
        CancellationToken ct)
    {
        if (ownerIdNullable is null)
        {
            _logger.LogWarning(
                "Lobby {LobbyId} has no owner — reverting InGame to ReadyChecking.", lobbyId);
            await RevertToReadyCheckingAsync(lobbyId, ct).ConfigureAwait(false);
            return LobbyState.ReadyChecking;
        }

        var ownerId = ownerIdNullable.Value;

        // Fetch the lobby from DB to get LadderId and RegionName (not in member snapshot).
        var lobbyRow = await _ctx.Set<LobbyEntity>()
            .FirstOrDefaultAsync(l => l.Id == lobbyId, ct)
            .ConfigureAwait(false);

        if (lobbyRow?.LadderId is null)
        {
            _logger.LogWarning(
                "Lobby {LobbyId} has no LadderId — reverting InGame to ReadyChecking.", lobbyId);
            await RevertToReadyCheckingAsync(lobbyId, ct).ConfigureAwait(false);
            return LobbyState.ReadyChecking;
        }

        // 1. Create a Matchmaking Party owned by the lobby owner and add all non-owner members.
        //    Wrapped in try/catch: if CreateAsync or any JoinAsync throws (e.g. PartyConflict
        //    or any transient error), revert the optimistic InGame state before propagating so
        //    the lobby is never permanently stranded (CR-02).
        var nonOwnerMembers = members
            .Where(m => m.PlayerId != ownerId)
            .ToList();

        MatchmakingParty party;
        try
        {
            party = await _partyService.CreateAsync(ownerId, ct).ConfigureAwait(false);

            // Add every non-owner lobby member to the party by party code.
            // JoinAsync looks up by PartyCode (citext — case-insensitive).
            foreach (var member in nonOwnerMembers)
            {
                await _partyService.JoinAsync(party.PartyCode, member.PlayerId, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Lobby {LobbyId} party creation/join failed — reverting InGame to ReadyChecking.", lobbyId);
            await RevertToReadyCheckingAsync(lobbyId, ct).ConfigureAwait(false);
            return LobbyState.ReadyChecking;
        }

        // 2. Enqueue the party ticket on the matchmaking service.
        var poolName = lobbyRow.RegionName ?? _options.DefaultPoolName;
        var result = await _matchmakingService
            .EnqueueAsync(ownerId, lobbyRow.LadderId.Value, poolName, party.Id, ct)
            .ConfigureAwait(false);

        if (result.Outcome == EnqueueOutcome.Queued)
        {
            // Success — InGame was already set in the lobby tx; log and return.
            _logger.LogInformation(
                "Lobby {LobbyId} entered InGame — party {PartyId} ticket {TicketId}.",
                lobbyId, party.Id, result.TicketId);
            return LobbyState.InGame;
        }
        else
        {
            // Rejection — revert the optimistic InGame back to ReadyChecking.
            _logger.LogWarning(
                "Lobby {LobbyId} matchmaking submission rejected: {Outcome} — {Detail}. Reverting to ReadyChecking.",
                lobbyId, result.Outcome, result.Detail);
            await RevertToReadyCheckingAsync(lobbyId, ct).ConfigureAwait(false);
            return LobbyState.ReadyChecking;
        }
    }

    /// <summary>
    /// Reverts the lobby state from <c>InGame</c> back to <c>ReadyChecking</c> when
    /// matchmaking submission was rejected or a precondition failed after the optimistic
    /// <c>InGame</c> commit in the lobby SERIALIZABLE transaction.
    /// </summary>
    private async Task RevertToReadyCheckingAsync(Guid lobbyId, CancellationToken ct)
    {
        _ctx.ChangeTracker.Clear();
        var lobby = await _ctx.Set<LobbyEntity>()
            .FirstOrDefaultAsync(l => l.Id == lobbyId, ct)
            .ConfigureAwait(false);
        if (lobby is not null)
        {
            lobby.State = LobbyState.ReadyChecking;
            lobby.UpdatedAt = DateTimeOffset.UtcNow;
            await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
