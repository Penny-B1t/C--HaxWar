namespace HexWar.Domain.Commands;

using HexWar.Domain.ValueObjects;
using System.Collections.Generic;

public record MoveCommand(NodeId From, NodeId To, List<int> UnitIds);
