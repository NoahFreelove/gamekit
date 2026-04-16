// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Xunit;

namespace GameKit.Core.Tests.Entities;

public class GameSessionStateMachineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // --- Valid transitions ---

    [Fact]
    public void Start_From_Pending_Transitions_To_Active()
    {
        var session = new GameSession();
        Assert.Equal(GameSessionState.Pending, session.State);

        session.Start(Now);

        Assert.Equal(GameSessionState.Active, session.State);
        Assert.Equal(Now, session.StartedAt);
    }

    [Fact]
    public void Complete_From_Active_Transitions_To_Completed()
    {
        var session = new GameSession();
        session.Start(Now);

        session.Complete(Now);

        Assert.Equal(GameSessionState.Completed, session.State);
        Assert.Equal(Now, session.CompletedAt);
    }

    [Fact]
    public void Cancel_From_Pending_Transitions_To_Cancelled()
    {
        var session = new GameSession();

        session.Cancel(Now);

        Assert.Equal(GameSessionState.Cancelled, session.State);
        Assert.Equal(Now, session.CompletedAt);
    }

    [Fact]
    public void Cancel_From_Active_Transitions_To_Cancelled()
    {
        var session = new GameSession();
        session.Start(Now);

        session.Cancel(Now);

        Assert.Equal(GameSessionState.Cancelled, session.State);
    }

    [Fact]
    public void Abandon_From_Active_Transitions_To_Abandoned()
    {
        var session = new GameSession();
        session.Start(Now);

        session.Abandon(Now);

        Assert.Equal(GameSessionState.Abandoned, session.State);
        Assert.Equal(Now, session.CompletedAt);
    }

    // --- Invalid transitions ---

    [Fact]
    public void Start_From_Active_Throws()
    {
        var session = new GameSession();
        session.Start(Now);

        var ex = Assert.Throws<InvalidGameSessionTransitionException>(() => session.Start(Now));
        Assert.Equal(GameSessionState.Active, ex.From);
        Assert.Equal(GameSessionState.Active, ex.To);
    }

    [Fact]
    public void Complete_From_Pending_Throws()
    {
        var session = new GameSession();

        var ex = Assert.Throws<InvalidGameSessionTransitionException>(() => session.Complete(Now));
        Assert.Equal(GameSessionState.Pending, ex.From);
        Assert.Equal(GameSessionState.Completed, ex.To);
    }

    [Fact]
    public void Abandon_From_Pending_Throws()
    {
        var session = new GameSession();

        var ex = Assert.Throws<InvalidGameSessionTransitionException>(() => session.Abandon(Now));
        Assert.Equal(GameSessionState.Pending, ex.From);
        Assert.Equal(GameSessionState.Abandoned, ex.To);
    }

    [Theory]
    [InlineData(GameSessionState.Completed)]
    [InlineData(GameSessionState.Cancelled)]
    [InlineData(GameSessionState.Abandoned)]
    public void Terminal_States_Cannot_Transition_To_Start(GameSessionState terminalState)
    {
        var session = new GameSession { State = terminalState };

        Assert.Throws<InvalidGameSessionTransitionException>(() => session.Start(Now));
    }

    [Theory]
    [InlineData(GameSessionState.Completed)]
    [InlineData(GameSessionState.Cancelled)]
    [InlineData(GameSessionState.Abandoned)]
    public void Terminal_States_Cannot_Transition_To_Complete(GameSessionState terminalState)
    {
        var session = new GameSession { State = terminalState };

        Assert.Throws<InvalidGameSessionTransitionException>(() => session.Complete(Now));
    }

    [Theory]
    [InlineData(GameSessionState.Completed)]
    [InlineData(GameSessionState.Cancelled)]
    [InlineData(GameSessionState.Abandoned)]
    public void Terminal_States_Cannot_Transition_To_Cancel(GameSessionState terminalState)
    {
        var session = new GameSession { State = terminalState };

        Assert.Throws<InvalidGameSessionTransitionException>(() => session.Cancel(Now));
    }

    [Theory]
    [InlineData(GameSessionState.Completed)]
    [InlineData(GameSessionState.Cancelled)]
    [InlineData(GameSessionState.Abandoned)]
    public void Terminal_States_Cannot_Transition_To_Abandon(GameSessionState terminalState)
    {
        var session = new GameSession { State = terminalState };

        Assert.Throws<InvalidGameSessionTransitionException>(() => session.Abandon(Now));
    }

    // --- Enum values ---

    [Fact]
    public void GameSessionState_Has_All_Expected_Values()
    {
        Assert.Equal(0, (int)GameSessionState.Pending);
        Assert.Equal(1, (int)GameSessionState.Active);
        Assert.Equal(2, (int)GameSessionState.Completed);
        Assert.Equal(3, (int)GameSessionState.Cancelled);
        Assert.Equal(4, (int)GameSessionState.Abandoned);
    }

    // --- Exception properties ---

    [Fact]
    public void InvalidGameSessionTransitionException_Has_From_And_To()
    {
        var ex = new InvalidGameSessionTransitionException(GameSessionState.Pending, GameSessionState.Completed);

        Assert.Equal(GameSessionState.Pending, ex.From);
        Assert.Equal(GameSessionState.Completed, ex.To);
        Assert.Contains("Pending", ex.Message);
        Assert.Contains("Completed", ex.Message);
    }
}
