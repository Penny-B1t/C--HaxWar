namespace HexWar.Domain.Entities;

using HexWar.Domain.Commands;
using HexWar.Domain.Enums;
using HexWar.Domain.Events;
using HexWar.Domain.Exceptions;
using HexWar.Domain.ValueObjects;

public partial class GameRoom
{
    private List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private List<EncounterOccurred> _encounterEvents = new();
    private List<NodeOwnershipChanged> _ownershipChanges = new();
    private List<ArrivalRecord> _arrivalRecords = new();
    private List<MoveExecutionRecord> _moveExecutionRecords = new();



    private void RaiseEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    private void ClearEvents()
    {
        _domainEvents.Clear();
        _encounterEvents.Clear();
        _ownershipChanges.Clear();
        _arrivalRecords.Clear();
        _moveExecutionRecords.Clear();
    }

    private void RecordEncounter(EncounterOccurred encounter)
    {
        _encounterEvents.Add(encounter);
        RaiseEvent(encounter);
    }

    private void RecordOwnershipChange(NodeId nodeId, NodeOwnership previous, NodeOwnership current, bool isSupplyLine)
    {
        var change = new NodeOwnershipChanged(nodeId, previous, current, isSupplyLine);
        _ownershipChanges.Add(change);
    }

    private void RecordArrival(NodeId destination, PlayerSide side, int count, EdgeId viaEdge)
    {
        _arrivalRecords.Add(new ArrivalRecord(destination, side, count, viaEdge));
    }

    private void RecordMoveExecution(PlayerSide side, NodeId from, NodeId to, int count)
    {
        _moveExecutionRecords.Add(new MoveExecutionRecord(from, to, side, count));
    }

    // 
    public RoundResolutionResult ResolveRound()
    {
        if (Phase != GamePhase.Planning)
            throw new DomainException("Cannot resolve round outside Planning phase");

        Phase = GamePhase.Resolution;
        ClearEvents();
        var result = new RoundResolutionResult();

        // ============================================================
        // 1단계: 모든 예약된 이동을 실제로 실행
        // ============================================================
        foreach (var side in new[] { PlayerSide.A, PlayerSide.B })
        {
            foreach (var move in _pendingMoves[side])
            {
                var sourceNode = Nodes[move.From];

                // 이제 진짜로 유닛 차감
                sourceNode.DepartMobileUnits(side, move.Count);

                // 간선에 배치
                var edgeId = new EdgeId(move.From, move.To);
                Edges[edgeId].StartTravel(side, move.Count, move.To);

                // 출발 이벤트 (이미 Planning에서 발행했지만, 
                // Resolution에서도 실제 실행 기록으로 남김)
                RecordMoveExecution(side, move.From, move.To, move.Count);
            }
        }

        // ============================================================
        // 2단계: 모든 간선에서 1라운드 진행
        // ============================================================
        var arrivals = ProcessAllEdgeAdvances();

        // ============================================================
        // 3단계: 조우 확인
        // ============================================================
        var encounters = FindAllEncounters();

        foreach (var enc in encounters)
        {
            var edge = Edges[enc.EdgeId];
            var encounterEvent = new EncounterOccurred(
                RoomId, enc.EdgeId, edge.From, edge.To, enc.RemainingRounds,
                new EncounterParticipantInfo(enc.GroupA.Side, enc.GroupA.UnitCount),
                new EncounterParticipantInfo(enc.GroupB.Side, enc.GroupB.UnitCount),
                CurrentRound
            );
            RecordEncounter(encounterEvent);
            result.Encounters.Add(encounterEvent);
        }

        // ============================================================
        // 4단계: 조우 없는 유닛만 도착 처리
        // ============================================================
        var blockedUnits = GetBlockedUnits(encounters);

        foreach (var arrival in arrivals)
        {
            if (!blockedUnits.Contains(arrival))
            {
                var node = Nodes[arrival.Destination];
                var previousOwnership = node.Ownership;

                node.ArriveMobileUnits(arrival.Side, arrival.Count);

                RecordArrival(arrival.Destination, arrival.Side, arrival.Count, arrival.ViaEdge);

                if (node.Ownership != previousOwnership)
                {
                    RecordOwnershipChange(
                        arrival.Destination, previousOwnership,
                        node.Ownership, node.IsSupplyLine
                    );
                }

                RaiseEvent(new UnitsArrived(
                    RoomId, arrival.Destination, arrival.Side,
                    arrival.Count, arrival.ViaEdge, CurrentRound, node.Ownership
                ));

                result.ArrivedUnits.Add(arrival);
            }
        }

        // ============================================================
        // 5단계: 조우 Pending 등록
        // ============================================================
        foreach (var encEvent in _encounterEvents)
        {
            var edge = Edges[encEvent.EdgeId];
            var travelingGroups = edge.TravelingUnits[encEvent.RemainingRounds];

            var groupA = travelingGroups.First(g => g.Side == PlayerSide.A);
            var groupB = travelingGroups.First(g => g.Side == PlayerSide.B);

            var pending = new PendingEncounter(
                encEvent.EdgeId, groupA, groupB, encEvent.RemainingRounds
            );
            PendingEncounters.Add(pending);
            result.PendingEncounters.Add(pending);
        }

        // ============================================================
        // 6단계: 정리 및 다음 라운드 준비
        // ============================================================
        foreach (var node in Nodes.Values)
            node.ClearDepartureHistory();

        // 예약된 이동 초기화
        _pendingMoves[PlayerSide.A].Clear();
        _pendingMoves[PlayerSide.B].Clear();

        int completedRound = CurrentRound;
        CurrentRound++;
        UnitUsedThisRound[PlayerSide.A] = 0;
        UnitUsedThisRound[PlayerSide.B] = 0;

        // 라운드 해소 이벤트
        RaiseEvent(new RoundResolved(
            RoomId, completedRound, _arrivalRecords,
            _encounterEvents, _ownershipChanges, _moveExecutionRecords
        ));

        // ============================================================
        // 7단계: 게임 종료 체크
        // ============================================================
        if (CheckGameOver() || CurrentRound > MaxRounds)
        {
            Phase = GamePhase.GameOver;
            var winner = GetWinner();
            var reason = CurrentRound > MaxRounds
                ? GameOverReason.MaxRoundsReached
                : GameOverReason.AllNodesCaptured;

            var scores = new Dictionary<PlayerSide, int>
            {
                { PlayerSide.A, Nodes.Values.Count(n => n.Ownership == NodeOwnership.PlayerA) },
                { PlayerSide.B, Nodes.Values.Count(n => n.Ownership == NodeOwnership.PlayerB) }
            };

            RaiseEvent(new GameOver(RoomId, winner, reason, completedRound, scores));
            result.GameOver = true;
            result.Winner = winner;
        }
        else
        {
            Phase = GamePhase.Planning;
            RaiseEvent(new RoundStarted(RoomId, CurrentRound));
            result.GameOver = false;
        }

        return result;
    }
    // 수정된 MoveUnits 메서드
    // Planning 계획 단계에서 유닛 차감되는 문제 수정 
    public MoveResult MoveUnits(PlayerSide side, MoveCommand command)
    {
        if (Phase != GamePhase.Planning)
            throw new DomainException($"Cannot move in {Phase} phase");

        if (UnitUsedThisRound[side] >= MaxUnitsPerPlayer)
            throw new DomainException("All units already moved this round");

        // 이번 명령으로 사용할 유닛 수 (남은 유닛 수를 초과할 수 없음)
        int remainingUnits = MaxUnitsPerPlayer - UnitUsedThisRound[side];
        int actualCount = Math.Min(command.UnitCount, remainingUnits);

        if (actualCount <= 0)
            throw new DomainException("No units available to commit");

        ValidatePath(command.From, command.To);

        var sourceNode = Nodes[command.From];

        // 출발지에 실제로 유닛이 충분한지 확인만 (차감은 아직 안 함)
        int available = sourceNode.GetMobileCount(side);

        if (available < actualCount)
            throw new DomainException(
                $"Not enough units at {command.From}. Available: {available}, Requested: {actualCount}");

        // 예약 확정 (취소 불가)
        _pendingMoves[side].Add(new PendingMove(command.From, command.To, actualCount));
        UnitUsedThisRound[side] += actualCount;

        // 이벤트 발행
        RaiseEvent(new UnitsDeparted(RoomId, side, command.From, actualCount, CurrentRound));

        return new MoveResult(actualCount, command.From, command.To);
    }

    // 
    public void ResolveEncounter(EdgeId edgeId, PlayerSide deciderSide, EncounterDecision decision)
    {
        if (Phase != GamePhase.Planning)
            throw new DomainException($"Encounter decisions must be made during Planning phase");

        var pending = PendingEncounters.FirstOrDefault(e => e.EdgeId == edgeId);
        if (pending == null)
            throw new DomainException($"No pending encounter on edge {edgeId}");

        // 이미 결정했는지 확인 (번복 불가)
        if (pending.HasDecided(deciderSide))
            throw new DomainException("Decision already made for this encounter and cannot be changed");

        // 결정 처리
        var edge = Edges[edgeId];
        var decidingGroup = pending.GroupA.Side == deciderSide ? pending.GroupA : pending.GroupB;

        EncounterOutcome outcome;
        if (decision == EncounterDecision.Retreat)
        {
            NodeId retreatDestination = edge.Id.GetOppositeNode(decidingGroup.Destination);
            edge.RemoveTravelingGroup(deciderSide);
            edge.StartTravel(deciderSide, decidingGroup.UnitCount, retreatDestination);

            outcome = new EncounterOutcome(
                deciderSide,
                retreatDestination,
                decidingGroup.UnitCount,
                edge.Distance.RoundsRequired
            );

            RaiseEvent(new UnitsRetreated(RoomId, deciderSide, edgeId, decidingGroup.UnitCount, CurrentRound));
        }
        else
        {
            outcome = new EncounterOutcome(
                deciderSide,
                decidingGroup.Destination,
                decidingGroup.UnitCount,
                pending.RemainingRounds
            );

            RaiseEvent(new UnitsAdvanced(RoomId, deciderSide, edgeId, decidingGroup.UnitCount, CurrentRound));
        }

        // 결정 확정 (변경 불가)
        pending.MarkDecided(deciderSide, decision, outcome);

        // 비공개 이벤트
        RaiseEvent(new EncounterDecisionMade(
            RoomId, deciderSide, edgeId, decision, decidingGroup.UnitCount, CurrentRound
        ));

        // 양측 모두 결정 완료
        if (pending.BothDecided)
        {
            PendingEncounters.Remove(pending);
            RaiseEvent(new EncounterResolved(
                RoomId, edgeId,
                pending.DecisionA!.Value, pending.DecisionB!.Value,
                pending.OutcomeA!, pending.OutcomeB!,
                CurrentRound
            ));
        }
    }

    // 미사용 메서드 추후 삭제 여부 결정 
    private NodeId GetRetreatDestination(Edge edge, PlayerSide side)
    {
        // 가장 먼저 도착한 이동 그룹을 찾음
        // travelingUnits의 키값은 라운드 수
        // 가장 작은 키값을 가진 그룹이 가장 먼저 도착
        // Key 라운드 순으로 자동 정렬되는 자료형 사용 (SortedList)
        var group = edge.TravelingUnits
            .SelectMany(kvp => kvp.Value)
            .FirstOrDefault(g => g.Side == side);

        if (group == null)
            throw new InvalidOperationException($"No traveling group found for {side} on edge {edge.Id}");

        // 목적지의 반대편 노드가 출발지
        return edge.Id.GetOppositeNode(group.Destination);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
        _encounterEvents.Clear();
        _ownershipChanges.Clear();
        _arrivalRecords.Clear();
    }
}