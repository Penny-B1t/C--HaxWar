namespace HexWar.Domain.Entities;

using HexWar.Domain.Enums;
using HexWar.Domain.ValueObjects;

public class Node
{
    public NodeId Id { get; }
    public string Name { get; }
    public bool IsHeadquarters { get; }

    // 소유권 상태 
    // read-only 객체이지만 명시적으로 private set으로 변경
    public NodeOwnership Ownership { get; private set; }

    // 각 플레이어 별 유닛 배치 현황
    public Dictionary<PlayerSide, int> StationedUnits { get; } = new()
    {
        { PlayerSide.A , 0 },
        { PlayerSide.B , 0 }
    };

    public List<NodeId> Neighbors { get; } = new();

    public Node(NodeId id, string name, bool isHeadquarters = false)
    {
        Id = id;
        Name = name;
        IsHeadquarters = isHeadquarters;
        Ownership = NodeOwnership.Neutral;
    }

    // 유닛이 도착한 경우 호출하는 메서드
    public void ArriveUnits(PlayerSide side, int count)
    {
        // 0 이상의 값만 통과할 수 있도록 검증
        if (count <= 0) throw new ArgumentException("Unit count must be positive");
        StationedUnits[side] += count;
        UpdateOwnership();
    }

    // 유닛이 이동할 경우 호출하는 메서드
    public void DepartUnits(PlayerSide side, int count)
    {
        // 이동 병력이 충분한지 검사
        if (StationedUnits[side] < count)
            throw new InvalidOperationException($"Not enough units at node {Id}. Has: {StationedUnits[side]}, Requested: {count}");

        StationedUnits[side] -= count;
        UpdateOwnership();
    }

    public void UpdateOwnership()
    {
        // 분부 여부 확인 및 조기 탈출
        if (IsHeadquarters)
        {
            Ownership = NodeOwnership.Neutral; // 본부는 항상 중립
            return;
        }

        int countA = StationedUnits[PlayerSide.A];
        int countB = StationedUnits[PlayerSide.B];

        if (countA == 0 && countB == 0)
        {
            Ownership = NodeOwnership.Neutral;
        }
        else if (countA > countB)
        {
            Ownership = NodeOwnership.PlayerA;
        }
        else if (countA < countB)
        {
            Ownership = NodeOwnership.PlayerB;
        }
        else
        {
            Ownership = NodeOwnership.Contested;
        }

    }











}