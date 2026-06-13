using static HexWar.Domain.Entities.GameRoom;
namespace HexWar.Benchmarks;

using HexWar.Domain.Commands;
using HexWar.Domain.Entities;
using HexWar.Domain.Enums;
using HexWar.Domain.Exceptions;
using HexWar.Domain.ValueObjects;

/// <summary>
/// 실제 게임과 동일한 제약 조건으로 게임을 시뮬레이션합니다.
/// - MaxRounds = 20 (GameRoom 기본값)
/// - 유닛 3기 제한
/// - 유닛이 있는 노드에서만 이동
/// - 도착 노드의 유닛도 다음 라운드에 사용 가능
/// - 게임 종료 조건 적용
/// </summary>
public class RealisticGameSimulator
{
    private readonly Random _random;
    private readonly bool _verbose; // 플레이 처리 내용 출력 여부 

    public RealisticGameSimulator(int seed = 42, bool verbose = false)
    {
        _random = new Random(seed);
        _verbose = verbose;
    }

    /// <summary>
    /// 하나의 완전한 게임을 시뮬레이션합니다.
    /// 게임 종료(승리 조건 or MaxRounds)까지 진행합니다.
    /// </summary>
    public GameSimulationResult SimulateCompleteGame(bool trackGC = false)
    {
        var room = new GameRoom($"sim-{Guid.NewGuid():N}");
        room.InitializeMap();
        room.AddPlayer(new PlayerId("p1"));
        room.AddPlayer(new PlayerId("p2"));

        var roundSnapshots = new List<RoundSnapshot>();
        var gcSnapshots = new List<GCSnapshot>(); // GC 상태 스샷 
        int encounterCount = 0;
        int totalMovesA = 0;
        int totalMovesB = 0;

        for (int round = 1; round <= room.MaxRounds; round++)
        {
            if (room.Phase != GamePhase.Planning) break;

            // 라운드 시작 시점 상태 기록
            roundSnapshots.Add(CaptureSnapshot(room, round));

            if (trackGC && round % 10 == 0)
            {
                gcSnapshots.Add(new GCSnapshot
                {
                    Round = round,
                    Gen0 = GC.CollectionCount(0),
                    Gen1 = GC.CollectionCount(1),
                    Gen2 = GC.CollectionCount(2),
                    TotalMemory = GC.GetTotalMemory(false)
                });
            }

            // 각 플레이어가 이동 (실제 게임처럼 유닛 3기까지)
            int movesA = SimulatePlayerTurn(room, PlayerSide.A);
            int movesB = SimulatePlayerTurn(room, PlayerSide.B);
            totalMovesA += movesA;
            totalMovesB += movesB;

            // 라운드 해소
            var result = room.ResolveRound();
            encounterCount += result.Encounters.Count;

            // 조우 해소 (자동)
            ResolvePendingEncounters(room);

            // 게임 종료 체크
            if (result.GameOver) break;
        }

        // 최종 상태 기록
        roundSnapshots.Add(CaptureSnapshot(room, room.CurrentRound - 1));

        return new GameSimulationResult
        {
            TotalRounds = room.CurrentRound - 1,
            EncounterCount = encounterCount,
            TotalMovesA = totalMovesA,
            TotalMovesB = totalMovesB,
            Winner = GetWinner(room),
            FinalScores = GetScores(room),
            RoundSnapshots = roundSnapshots,
            GCSnapshots = gcSnapshots
        };
    }

    /// <summary>
    /// 한 플레이어의 전체 턴을 시뮬레이션합니다.
    /// 유닛 3기를 모두 사용하거나, 이동 가능한 유닛이 없을 때까지 이동합니다.
    /// </summary>
    private int SimulatePlayerTurn(GameRoom room, PlayerSide side)
    {
        int unitsUsed = 0;

        while (unitsUsed < GameRoom.MaxUnitsPerPlayer)
        {
            // 이동 가능한 유닛이 있는 노드들
            var availableNodes = room.Nodes.Values
                .Where(n => n.GetMobileCount(side) > 0)
                .Where(n => !n.IsHeadquarters) // 본부에서는 출발하지 않음
                .ToList();

            if (!availableNodes.Any()) break;

            // 노드 선택 (유닛이 많은 노드를 선호)
            var fromNode = SelectNodeWithStrategy(availableNodes, side, room);

            // 목적지 선택 (전략적 선택)
            var neighbors = fromNode.Neighbors.ToList();
            if (!neighbors.Any()) break;

            var toNodeId = SelectTargetWithStrategy(fromNode, neighbors, side, room);

            // 이동할 유닛 수 결정 (1~남은 유닛 수, 노드의 유닛 수를 초과하지 않음)
            int remainingMoves = GameRoom.MaxUnitsPerPlayer - unitsUsed;
            int availableAtNode = fromNode.GetMobileCount(side);
            int unitsToMove = Math.Min(remainingMoves, availableAtNode);
            unitsToMove = Math.Min(unitsToMove, _random.Next(1, unitsToMove + 1));

            try
            {
                room.MoveUnits(side, new MoveCommand(fromNode.Id, toNodeId, unitsToMove));
                unitsUsed += unitsToMove;

                if (_verbose)
                {
                    Console.WriteLine($"  {side}: {fromNode.Name}({fromNode.Id}) → " +
                                    $"{room.Nodes[toNodeId].Name}({toNodeId.Value}) {unitsToMove}기");
                }
            }
            catch (DomainException)
            {
                break;
            }
        }

        return unitsUsed;
    }

    private Node SelectNodeWithStrategy(List<Node> nodes, PlayerSide side, GameRoom room)
    {
        // 중립 노드로 이동할 수 있는 노드를 우선
        var strategicNodes = nodes.Where(n =>
            n.Neighbors.Any(neighborId =>
                room.Nodes[neighborId].Ownership == NodeOwnership.Neutral ||
                room.Nodes[neighborId].Ownership == NodeOwnership.Contested))
            .ToList();

        var candidates = strategicNodes.Any() ? strategicNodes : nodes;

        // 유닛 수가 많은 노드 우선 (가중치 랜덤)
        var totalUnits = candidates.Sum(n => n.GetMobileCount(side));
        if (totalUnits <= 0) return candidates.Last();

        var roll = _random.Next(totalUnits);
        int cumulative = 0;

        foreach (var node in candidates)
        {
            cumulative += node.GetMobileCount(side);
            if (roll < cumulative) return node;
        }

        return candidates.Last();
    }

    private NodeId SelectTargetWithStrategy(Node fromNode, List<NodeId> neighbors, PlayerSide side, GameRoom room)
    {
        var targetOwnership = side == PlayerSide.A ? NodeOwnership.PlayerB : NodeOwnership.PlayerA;

        // 본부가 아닌 이웃 노드 중에서 중립이나 상대 소유 노드를 선호
        var preferred = neighbors
            .Where(id => !room.Nodes[id].IsHeadquarters)
            .Where(id => room.Nodes[id].Ownership == NodeOwnership.Neutral || room.Nodes[id].Ownership == targetOwnership)
            .ToList();

        if (preferred.Any())
        {
            return preferred[_random.Next(preferred.Count)];
        }

        // 선호하는 노드가 없는 경우 본부가 아닌 이웃 노드 중에서 랜덤 선택
        var nonHqNeighbors = neighbors.Where(id => !room.Nodes[id].IsHeadquarters).ToList();
        if (nonHqNeighbors.Any())
        {
            return nonHqNeighbors[_random.Next(nonHqNeighbors.Count)];
        }

        return neighbors[_random.Next(neighbors.Count)];
    }

    private void ResolvePendingEncounters(GameRoom room)
    {
        foreach (var encounter in room.PendingEncounters.ToList())
        {
            // 각 플레이어가 랜덤하게 결정
            var decisionA = _random.Next(2) == 0 ? EncounterDecision.Advance : EncounterDecision.Retreat;
            var decisionB = _random.Next(2) == 0 ? EncounterDecision.Advance : EncounterDecision.Retreat;

            try
            {
                if (!encounter.HasDecided(PlayerSide.A))
                    room.ResolveEncounter(encounter.EdgeId, PlayerSide.A, decisionA);
                if (!encounter.HasDecided(PlayerSide.B))
                    room.ResolveEncounter(encounter.EdgeId, PlayerSide.B, decisionB);
            }
            catch (DomainException) { }
        }
    }

    private RoundSnapshot CaptureSnapshot(GameRoom room, int round)
    {
        return new RoundSnapshot
        {
            Round = round,
            Phase = room.Phase,
            NodeStates = room.Nodes.Values.Select(n => new NodeState
            {
                NodeId = n.Id.Value,
                Name = n.Name,
                Ownership = n.Ownership,
                UnitsA = n.GetTotalCount(PlayerSide.A),
                UnitsB = n.GetTotalCount(PlayerSide.B)
            }).ToList()
        };
    }

    private string GetWinner(GameRoom room)
    {
        int nodesA = room.Nodes.Values.Count(n => n.Ownership == NodeOwnership.PlayerA);
        int nodesB = room.Nodes.Values.Count(n => n.Ownership == NodeOwnership.PlayerB);
        if (nodesA > nodesB) return "A";
        if (nodesB > nodesA) return "B";
        return "Draw";
    }

    private Dictionary<string, int> GetScores(GameRoom room)
    {
        return new Dictionary<string, int>
        {
            { "A", room.Nodes.Values.Count(n => n.Ownership == NodeOwnership.PlayerA) },
            { "B", room.Nodes.Values.Count(n => n.Ownership == NodeOwnership.PlayerB) }
        };
    }
}

// ========================================================================
// 결과 타입
// ========================================================================

public class GameSimulationResult
{
    public int TotalRounds { get; set; }
    public int EncounterCount { get; set; }
    public int TotalMovesA { get; set; }
    public int TotalMovesB { get; set; }
    public string Winner { get; set; } = "Unknown";
    public Dictionary<string, int> FinalScores { get; set; } = new();
    public List<RoundSnapshot> RoundSnapshots { get; set; } = new();
    public List<GCSnapshot> GCSnapshots { get; set; } = new(); // 추가
}

public class GCSnapshot
{
    public int Round { get; set; }
    public int Gen0 { get; set; }
    public int Gen1 { get; set; }
    public int Gen2 { get; set; }
    public long TotalMemory { get; set; }
}

public class RoundSnapshot
{
    public int Round { get; set; }
    public GamePhase Phase { get; set; }
    public List<NodeState> NodeStates { get; set; } = new();
}

public class NodeState
{
    public int NodeId { get; set; }
    public string Name { get; set; } = "";
    public NodeOwnership Ownership { get; set; }
    public int UnitsA { get; set; }
    public int UnitsB { get; set; }
}
