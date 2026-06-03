namespace HexWar.Domain.Entities;

using HexWar.Domain.Enums;
using HexWar.Domain.ValueObjects;

public class TravelingGroup
{
    public PlayerSide Side { get; }
    public int UnitCount { get; }
    public NodeId Destination { get; }

    public TravelingGroup(PlayerSide side, int unitCount, NodeId destination)
    {
        Side = side;
        UnitCount = unitCount;
        Destination = destination;
    }
}