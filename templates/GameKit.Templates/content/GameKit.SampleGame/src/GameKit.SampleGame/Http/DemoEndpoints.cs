// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using GameKit.SampleGame.Game;

namespace GameKit.SampleGame.Http;

/// <summary>
/// Phase-2 demo endpoints for GameKit.SampleGame. Player registration has moved to
/// <c>GameKit.Auth</c> (<c>/auth/register</c>, <c>/auth/login/guest</c>, <c>/auth/login/password</c>);
/// only the game-session endpoints live here now. The Phase-1 anonymous
/// player-register route was intentionally removed when <c>GameKit.Auth</c> landed
/// (plan 02-08).
/// </summary>
public static class DemoEndpoints
{
    /// <summary>Maps the <c>/demo/*</c> endpoint group (game create / get / move).</summary>
    public static IEndpointRouteBuilder MapDemo(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/demo").WithTags("GameKit.SampleGame.Demo");

        // The Phase-1 anonymous player-register route was removed in Phase 2 (plan 02-08).
        // Clients now drive /auth/register + /auth/login/guest + /auth/login/password
        // directly; the resulting JWT carries `sub` = player id for use in the game
        // endpoints below.
        group.MapPost("/games", CreateGameAsync);
        group.MapGet("/games/{id:guid}", GetGameAsync);
        group.MapPost("/games/{id:guid}/moves", ApplyMoveAsync);

        return routes;
    }

    private static async Task<IResult> CreateGameAsync(
        CreateGameRequest req,
        GameKitDbContext db,
        IClock clock,
        IIdGenerator ids,
        IPlayerDisplayNameResolver names,
        CancellationToken ct)
    {
        if (req is null)
            return Results.BadRequest(new { error = "body is required" });
        if (req.PlayerXId == Guid.Empty || req.PlayerOId == Guid.Empty)
            return Results.BadRequest(new { error = "playerXId and playerOId are required" });
        if (req.PlayerXId == req.PlayerOId)
            return Results.BadRequest(new { error = "players must be different" });

        var hasX = await db.Players.AsNoTracking().AnyAsync(p => p.Id == req.PlayerXId, ct).ConfigureAwait(false);
        var hasO = await db.Players.AsNoTracking().AnyAsync(p => p.Id == req.PlayerOId, ct).ConfigureAwait(false);
        if (!hasX || !hasO)
            return Results.NotFound(new { error = "one or both players not found" });

        var now = clock.UtcNow;
        var session = new GameSession
        {
            Id = ids.NewId(),
            CreatedAt = now,
        };
        session.Start(now);
        session.Metadata = TicTacToeBoardSerializer.ToJsonDocument(TicTacToeBoard.NewEmpty());

        var partX = new SessionParticipant
        {
            Id = ids.NewId(),
            SessionId = session.Id,
            PlayerId = req.PlayerXId,
            Team = 0,
        };
        var partO = new SessionParticipant
        {
            Id = ids.NewId(),
            SessionId = session.Id,
            PlayerId = req.PlayerOId,
            Team = 1,
        };

        db.GameSessions.Add(session);
        db.SessionParticipants.Add(partX);
        db.SessionParticipants.Add(partO);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            return Results.Problem(
                title: "failed to create game",
                detail: ex.InnerException?.Message ?? ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var response = await BuildResponseAsync(session, new[] { partX, partO }, names, ct).ConfigureAwait(false);
        return Results.Created($"/demo/games/{session.Id}", response);
    }

    private static async Task<IResult> GetGameAsync(
        Guid id,
        GameKitDbContext db,
        IPlayerDisplayNameResolver names,
        CancellationToken ct)
    {
        var session = await db.GameSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (session is null)
            return Results.NotFound(new { error = "game not found" });

        var participants = await db.SessionParticipants
            .AsNoTracking()
            .Where(p => p.SessionId == id)
            .OrderBy(p => p.Team)
            .ToArrayAsync(ct)
            .ConfigureAwait(false);

        var response = await BuildResponseAsync(session, participants, names, ct).ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> ApplyMoveAsync(
        Guid id,
        MoveRequest req,
        GameKitDbContext db,
        IClock clock,
        IPlayerDisplayNameResolver names,
        CancellationToken ct)
    {
        if (req is null)
            return Results.BadRequest(new { error = "body is required" });

        var session = await db.GameSessions.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (session is null)
            return Results.NotFound(new { error = "game not found" });

        if (session.State != GameSessionState.Active)
            return Results.BadRequest(new { error = "game not active" });

        var participants = await db.SessionParticipants
            .Where(p => p.SessionId == id)
            .ToArrayAsync(ct)
            .ConfigureAwait(false);

        var participant = participants.FirstOrDefault(p => p.PlayerId == req.PlayerId);
        if (participant is null)
            return Results.BadRequest(new { error = "not a participant" });

        var mark = participant.Team switch
        {
            0 => Mark.X,
            1 => Mark.O,
            _ => Mark.None,
        };
        if (mark == Mark.None)
            return Results.BadRequest(new { error = "invalid team" });

        if (session.Metadata is null)
            return Results.Problem(
                title: "board missing",
                detail: "session metadata did not contain a board",
                statusCode: StatusCodes.Status500InternalServerError);

        TicTacToeBoard board;
        try
        {
            board = TicTacToeBoardSerializer.FromJsonDocument(session.Metadata);
        }
        catch (System.IO.InvalidDataException ex)
        {
            return Results.Problem(
                title: "corrupt board",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        try
        {
            board.ApplyMove(req.Row, req.Col, mark);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.ParamName ?? "out of range" });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        // Replace metadata with the updated board.
        session.Metadata?.Dispose();
        session.Metadata = TicTacToeBoardSerializer.ToJsonDocument(board);

        if (board.Outcome != BoardOutcome.InProgress)
        {
            foreach (var p in participants)
            {
                p.Result = ResultFor(p.Team, board.Outcome);
            }
            session.Complete(clock.UtcNow);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            return Results.Problem(
                title: "failed to save move",
                detail: ex.InnerException?.Message ?? ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var response = await BuildResponseAsync(session, participants, names, ct).ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static SessionResult ResultFor(int team, BoardOutcome outcome) => outcome switch
    {
        BoardOutcome.Draw => SessionResult.Draw,
        BoardOutcome.XWins => team == 0 ? SessionResult.Win : SessionResult.Loss,
        BoardOutcome.OWins => team == 1 ? SessionResult.Win : SessionResult.Loss,
        _ => throw new InvalidOperationException($"cannot derive result from non-terminal outcome '{outcome}'"),
    };

    private static async Task<GameStateResponse> BuildResponseAsync(
        GameSession session,
        SessionParticipant[] participants,
        IPlayerDisplayNameResolver names,
        CancellationToken ct)
    {
        var board = session.Metadata is null
            ? TicTacToeBoard.NewEmpty()
            : TicTacToeBoardSerializer.FromJsonDocument(session.Metadata);

        var cells = new int[3][];
        for (var r = 0; r < 3; r++)
        {
            cells[r] = new int[3];
            for (var c = 0; c < 3; c++)
                cells[r][c] = (int)board.Cells[r, c];
        }

        var ordered = participants.OrderBy(p => p.Team).ToArray();
        var views = new ParticipantView[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            var p = ordered[i];
            var name = await names.ResolveAsync(p.PlayerId, ct).ConfigureAwait(false);
            views[i] = new ParticipantView(p.PlayerId, p.Team, name, p.Result?.ToString());
        }

        return new GameStateResponse(
            session.Id,
            session.State.ToString(),
            cells,
            board.WhoseTurn.ToString(),
            board.Outcome.ToString(),
            views);
    }
}
