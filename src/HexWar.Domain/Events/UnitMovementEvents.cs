namespace HexWar.Domain.Events;

using HexWar.Domain.Enums;
using HexWar.Domain.ValueObjects;

/// <summary>
/// 유닛 출발 이벤트 (공개 정보)
/// 상대방에게도 공개되는 정보로, 출발지와 유닛 수만 포함
/// </summary>
public sealed record UnitsDeparted : DomainEvent
{
    /// <summary>이동을 수행한 플레이어</summary>
    public PlayerSide Side { get; init; }

    /// <summary>출발 노드 ID</summary>
    public NodeId FromNode { get; init; }

    /// <summary>이동하는 유닛 수</summary>
    public int UnitCount { get; init; }

    /// <summary>이동이 시작된 라운드</summary>
    public int RoundNumber { get; init; }

    public UnitsDeparted(
        string roomId,
        PlayerSide side,
        NodeId fromNode,
        int unitCount,
        int roundNumber)
        : base(roomId)
    {
        Side = side;
        FromNode = fromNode;
        UnitCount = unitCount;
        RoundNumber = roundNumber;
    }

    /// <summary>
    /// 상대방에게 전송할 공개 정보만 포함된 DTO 반환
    /// 목적지 정보는 의도적으로 제외됨
    /// </summary>
    public UnitsDepartedPublicInfo ToPublicInfo()
    {
        return new UnitsDepartedPublicInfo(
            RoomId, Side, FromNode, UnitCount, RoundNumber
        );
    }
}

/// <summary>
/// 상대방에게 전송되는 유닛 출발 공개 정보
/// </summary>
public sealed record UnitsDepartedPublicInfo
{
    public string RoomId { get; init; }
    public PlayerSide Side { get; init; }
    public NodeId FromNode { get; init; }
    public int UnitCount { get; init; }
    public int RoundNumber { get; init; }

    public UnitsDepartedPublicInfo(
        string roomId, PlayerSide side, NodeId fromNode, int unitCount, int roundNumber)
    {
        RoomId = roomId;
        Side = side;
        FromNode = fromNode;
        UnitCount = unitCount;
        RoundNumber = roundNumber;
    }
}

/// <summary>
/// 유닛 도착 이벤트 (제한적 공개)
/// 본부 점유 중인 플레이어에게만 전체 정보가 공개됨
/// </summary>
public sealed record UnitsArrived : DomainEvent
{
    /// <summary>도착 노드 ID</summary>
    public NodeId DestinationNode { get; init; }

    /// <summary>유닛 소속</summary>
    public PlayerSide Side { get; init; }

    /// <summary>도착한 유닛 수</summary>
    public int UnitCount { get; init; }

    /// <summary>이동에 사용된 간선</summary>
    public EdgeId ViaEdge { get; init; }

    /// <summary>도착 완료된 라운드</summary>
    public int RoundNumber { get; init; }

    /// <summary>도착 후 노드의 새로운 점유 상태</summary>
    public NodeOwnership NewOwnership { get; init; }

    public UnitsArrived(
        string roomId,
        NodeId destinationNode,
        PlayerSide side,
        int unitCount,
        EdgeId viaEdge,
        int roundNumber,
        NodeOwnership newOwnership)
        : base(roomId)
    {
        DestinationNode = destinationNode;
        Side = side;
        UnitCount = unitCount;
        ViaEdge = viaEdge;
        RoundNumber = roundNumber;
        NewOwnership = newOwnership;
    }
}

/// <summary>
/// 유닛 복귀 이벤트 (공개)
/// 조우에서 복귀를 선택한 경우 발생
/// </summary>
public sealed record UnitsRetreated : DomainEvent
{
    /// <summary>복귀를 결정한 플레이어</summary>
    public PlayerSide Side { get; init; }

    /// <summary>조우가 발생한 간선</summary>
    public EdgeId FromEdge { get; init; }

    /// <summary>복귀하는 유닛 수</summary>
    public int UnitCount { get; init; }

    /// <summary>복귀가 발생한 라운드</summary>
    public int RoundNumber { get; init; }

    public UnitsRetreated(
        string roomId,
        PlayerSide side,
        EdgeId fromEdge,
        int unitCount,
        int roundNumber)
        : base(roomId)
    {
        Side = side;
        FromEdge = fromEdge;
        UnitCount = unitCount;
        RoundNumber = roundNumber;
    }
}

/// <summary>
/// 유닛 전진 지속 이벤트 (공개)
/// 조우에서 전진을 선택한 경우 발생
/// </summary>
public sealed record UnitsAdvanced : DomainEvent
{
    /// <summary>전진을 결정한 플레이어</summary>
    public PlayerSide Side { get; init; }

    /// <summary>조우가 발생한 간선</summary>
    public EdgeId OnEdge { get; init; }

    /// <summary>전진하는 유닛 수</summary>
    public int UnitCount { get; init; }

    /// <summary>전진 결정이 내려진 라운드</summary>
    public int RoundNumber { get; init; }

    public UnitsAdvanced(
        string roomId,
        PlayerSide side,
        EdgeId onEdge,
        int unitCount,
        int roundNumber)
        : base(roomId)
    {
        Side = side;
        OnEdge = onEdge;
        UnitCount = unitCount;
        RoundNumber = roundNumber;
    }
}

// 본부 점유 중인 경우 조건부 제공 정보 
public sealed record UnitsArrivedSummary : DomainEvent
{
    public NodeId DestinationNode { get; init; }
    public PlayerSide Side { get; init; }
    public int UnitCount { get; init; }
    public int RoundNumber { get; init; }

    public UnitsArrivedSummary(
        string roomId,
        NodeId destinationNode,
        PlayerSide side,
        int unitCount,
        int roundNumber)
        : base(roomId)
    {
        DestinationNode = destinationNode;
        Side = side;
        UnitCount = unitCount;
        RoundNumber = roundNumber;
    }
}

public sealed record MoveExecutionRecord
{
    public NodeId FromNode { get; init; }

    public NodeId ToNode { get; init; }

    public PlayerSide Side { get; init; }

    public int UnitCount { get; init; }

    public MoveExecutionRecord(NodeId fromNode, NodeId toNode, PlayerSide side, int unitCount)
    {
        FromNode = fromNode;
        ToNode = toNode;
        Side = side;
        UnitCount = unitCount;
    }
}