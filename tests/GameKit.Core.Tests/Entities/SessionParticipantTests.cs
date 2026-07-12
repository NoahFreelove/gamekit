// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Xunit;

namespace GameKit.Core.Tests.Entities;

public class SessionParticipantTests
{
    [Fact]
    public void PlayerId_Is_Nullable_Guid()
    {
        // GDPR tombstone: PlayerId must be Guid? (nullable)
        var prop = typeof(SessionParticipant).GetProperty(nameof(SessionParticipant.PlayerId));
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid?), prop!.PropertyType);
    }

    [Fact]
    public void Can_Set_PlayerId_To_Null()
    {
        var participant = new SessionParticipant
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            Team = 0
        };

        participant.PlayerId = null;
        Assert.Null(participant.PlayerId);
    }

    [Fact]
    public void Result_Is_Nullable_SessionResult()
    {
        var prop = typeof(SessionParticipant).GetProperty(nameof(SessionParticipant.Result));
        Assert.NotNull(prop);
        Assert.Equal(typeof(SessionResult?), prop!.PropertyType);
    }

    [Fact]
    public void Rating_Fields_Are_Nullable_Doubles()
    {
        var participant = new SessionParticipant
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            Team = 0
        };

        Assert.Null(participant.RatingBefore);
        Assert.Null(participant.RatingAfter);
        Assert.Null(participant.RatingDelta);

        participant.RatingBefore = 1500.0;
        participant.RatingAfter = 1520.0;
        participant.RatingDelta = 20.0;

        Assert.Equal(1500.0, participant.RatingBefore);
        Assert.Equal(1520.0, participant.RatingAfter);
        Assert.Equal(20.0, participant.RatingDelta);
    }

    [Fact]
    public void SessionResult_Has_All_Expected_Values()
    {
        Assert.Equal(0, (int)SessionResult.Win);
        Assert.Equal(1, (int)SessionResult.Loss);
        Assert.Equal(2, (int)SessionResult.Draw);
        Assert.Equal(3, (int)SessionResult.Abandoned);
    }
}
