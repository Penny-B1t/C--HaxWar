namespace HexWar.Domain.Events;

using HexWar.Domain.Enums;
using HexWar.Domain.ValueObjects;

/// <summary>
/// 게임 시작 이벤트
/// 양 플레이어가 모두 참가하여 첫 라운드가 시작됨
/// </summary>
public sealed record GameStarted : DomainEvent
{
    /// <summary>Player A의 ID</summary>
    public PlayerId PlayerAId { get; init; }

    /// <summary>Player B의 ID</summary>
    public PlayerId PlayerBId { get; init; }

    /// <summary>시작 라운드 번호 (항상 1)</summary>
    public int StartingRound { get; init; }

    public GameStarted(string roomId, PlayerId playerAId, PlayerId playerBId)
        : base(roomId)
    {
        PlayerAId = playerAId;
        PlayerBId = playerBId;
        StartingRound = 1;
    }
}

/// <summary>
/// 라운드 시작 이벤트
/// 새로운 계획 단계가 시작됨
/// </summary>
public sealed record RoundStarted : DomainEvent
{
    /// <summary>시작된 라운드 번호</summary>
    public int RoundNumber { get; init; }

    /// <summary>현재 게임 단계</summary>
    public GamePhase Phase { get; init; }

    public RoundStarted(string roomId, int roundNumber)
        : base(roomId)
    {
        RoundNumber = roundNumber;
        Phase = GamePhase.Planning;
    }
}

/// <summary>
/// 라운드 해소 완료 이벤트
/// 모든 이동이 처리되고 점유권이 갱신됨
/// </summary>
public sealed record RoundResolved : DomainEvent
{
    /// <summary>완료된 라운드 번호</summary>
    public int CompletedRound { get; init; }

    /// <summary>이 라운드에 도착한 유닛들</summary>
    public IReadOnlyList<ArrivalRecord> Arrivals { get; init; }

    /// <summary>발생한 조우 목록</summary>
    public IReadOnlyList<EncounterOccurred> Encounters { get; init; }

    /// <summary>변경된 노드 점유 상태</summary>
    public IReadOnlyList<NodeOwnershipChanged> OwnershipChanges { get; init; }

    public IReadOnlyList<MoveExecutionRecord> MoveExecutions { get; init; }

    public RoundResolved(
        string roomId,
        int completedRound,
        IEnumerable<ArrivalRecord> arrivals,
        IEnumerable<EncounterOccurred> encounters,
        IEnumerable<NodeOwnershipChanged> ownershipChanges,
        IEnumerable<MoveExecutionRecord> moveExecutions)  // ← 추가
        : base(roomId)
    {
        CompletedRound = completedRound;
        Arrivals = arrivals.ToList().AsReadOnly();
        Encounters = encounters.ToList().AsReadOnly();
        OwnershipChanges = ownershipChanges.ToList().AsReadOnly();
        MoveExecutions = moveExecutions.ToList().AsReadOnly();
    }
}

/// <summary>
/// 게임 종료 이벤트
/// 승리 조건 달성 또는 최대 라운드 도달
/// </summary>
public sealed record GameOver : DomainEvent
{
    /// <summary>승리한 플레이어 (무승부 시 null)</summary>
    public PlayerSide? Winner { get; init; }

    /// <summary>종료 사유</summary>
    public GameOverReason Reason { get; init; }

    /// <summary>종료 당시 라운드</summary>
    public int FinalRound { get; init; }

    /// <summary>최종 점수</summary>
    public IReadOnlyDictionary<PlayerSide, int> FinalScores { get; init; }

    public GameOver(
        string roomId,
        PlayerSide? winner,
        GameOverReason reason,
        int finalRound,
        Dictionary<PlayerSide, int> finalScores)
        : base(roomId)
    {
        Winner = winner;
        Reason = reason;
        FinalRound = finalRound;
        FinalScores = finalScores.AsReadOnly();
    }
}

/// <summary>
/// 게임 종료 사유
/// </summary>
public enum GameOverReason
{
    /// <summary>한 플레이어가 본부를 제외한 모든 노드를 점령</summary>
    AllNodesCaptured,

    /// <summary>최대 라운드 도달</summary>
    MaxRoundsReached,

    /// <summary>플레이어 연결 끊김</summary>
    PlayerDisconnected,

    /// <summary>플레이어 항복</summary>
    PlayerSurrendered
}

/// <summary>
/// 노드 점유 상태 변경 기록
/// </summary>
public sealed record NodeOwnershipChanged
{
    public NodeId NodeId { get; init; }
    public NodeOwnership PreviousOwnership { get; init; }
    public NodeOwnership NewOwnership { get; init; }
    public bool IsSupplyLineActive { get; init; }

    public NodeOwnershipChanged(
        NodeId nodeId,
        NodeOwnership previousOwnership,
        NodeOwnership newOwnership,
        bool isSupplyLineActive)
    {
        NodeId = nodeId;
        PreviousOwnership = previousOwnership;
        NewOwnership = newOwnership;
        IsSupplyLineActive = isSupplyLineActive;
    }
}

/// <summary>
/// 유닛 도착 기록
/// </summary>
public sealed record ArrivalRecord
{
    public NodeId DestinationNodeId { get; init; }
    public PlayerSide Side { get; init; }
    public int UnitCount { get; init; }
    public EdgeId ViaEdge { get; init; }

    public ArrivalRecord(NodeId destinationNodeId, PlayerSide side, int unitCount, EdgeId viaEdge)
    {
        DestinationNodeId = destinationNodeId;
        Side = side;
        UnitCount = unitCount;
        ViaEdge = viaEdge;
    }
}