namespace HexWar.Domain.Enums;

public static class NodeOwnershipExtensions
{
    public static NodeOwnership FromPlayerSide(PlayerSide side) => side switch
    {
        PlayerSide.A => NodeOwnership.PlayerA,
        PlayerSide.B => NodeOwnership.PlayerB,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };
}