using System.Collections.Generic;
using HexWar.Domain.Commands;
using HexWar.Domain.Entities;
using HexWar.Domain.Enums;
using HexWar.Domain.ValueObjects;
using Xunit;

namespace HexWar.Domain.Tests;

public class GameRoomTests
{
    private readonly GameRoom _room;

    public GameRoomTests()
    {
        _room = new GameRoom("test-room");
        _room.InitializeMap();
    }

    [Fact]
    public void AddPlayer_ShouldSetInitialUnitsAtStartNode()
    {
        var side = _room.AddPlayer(new PlayerId("player1"));

        Assert.Equal(PlayerSide.A, side);
        Assert.Equal(3, _room.Nodes[new NodeId(1)].GetTotalCount(PlayerSide.A));
        Assert.Equal(NodeOwnership.PlayerA, _room.Nodes[new NodeId(1)].Ownership);
    }

    [Fact]
    public void MoveUnits_ShouldDeductFromSourceAndTravel()
    {
        _room.AddPlayer(new PlayerId("p1"));
        _room.AddPlayer(new PlayerId("p2"));
        // 현재 Phase: Planning

        var cmd = new MoveCommand(new NodeId(1), new NodeId(4), 2);
        _room.MoveUnits(PlayerSide.A, cmd);

        // Planning 단계에서는 아직 차감 및 이동이 시작되지 않음
        Assert.Equal(3, _room.Nodes[new NodeId(1)].GetTotalCount(PlayerSide.A));
        Assert.Equal(2, _room.UnitUsedThisRound[PlayerSide.A]);

        // ResolveRound 실행 후 실제 이동 발생
        _room.ResolveRound();

        Assert.Equal(1, _room.Nodes[new NodeId(1)].GetTotalCount(PlayerSide.A));
        var edgeId = new EdgeId(new NodeId(1), new NodeId(4));
        Assert.NotEmpty(_room.Edges[edgeId].TravelingUnits);
    }

    [Fact]
    public void SameNode_EqualUnits_ShouldBeContested()
    {
        var node = _room.Nodes[new NodeId(2)];
        node.ArriveMobileUnits(PlayerSide.A, 2);
        node.ArriveMobileUnits(PlayerSide.B, 2);

        Assert.Equal(NodeOwnership.Contested, node.Ownership);
    }

    [Fact]
    public void Headquarters_ShouldAlwaysBeNeutral()
    {
        var hq = _room.Nodes[new NodeId(6)];
        hq.ArriveMobileUnits(PlayerSide.A, 10);

        Assert.Equal(NodeOwnership.Neutral, hq.Ownership);
    }
}