namespace HexWar.Domain.Events;

using HexWar.Domain.Entities;
using HexWar.Domain.Enums;
using HexWar.Domain.ValueObjects;

public interface IDomainEvent { }

public record GameStarted(string RoomId) : IDomainEvent;
public record UnitsMoveStarted(string RoomId, PlayerSide Side, NodeId From, NodeId To, int Count, int Round) : IDomainEvent;
public record EncounterEvent(EdgeId EdgeId, TravelingGroup GroupA, TravelingGroup GroupB, int RemainingRounds) : IDomainEvent;
public record RoundResolved(string RoomId, int RoundCompleted) : IDomainEvent;
public record GameOver(string RoomId, PlayerSide? Winner) : IDomainEvent;