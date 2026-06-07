namespace HexWar.Matchmaking.Services;

using Grpc.Core;
using HexWar.Application.Sessions;
using HexWar.Matchmaking;

/// <summary>
/// gRPC 게임룸 정보 서비스
/// 
/// HexWar.Matchmaking 클래스 명칭은 .proto 파일의 첫 번째 줄에서 설정한다.
/// package HexWar.Matchmaking; 
/// </summary>
public class GameRoomService : HexWar.Matchmaking.GameRoomService.GameRoomServiceBase
{
    private readonly SessionRegistry _sessionRegistry;

    public GameRoomService(SessionRegistry sessionRegistry)
    {
        _sessionRegistry = sessionRegistry;
    }

    public override Task<RoomInfo> GetRoomInfo(
        GetRoomInfoRequest request, ServerCallContext context)
    {
        var session = _sessionRegistry.GetSession(request.RoomId);
        
        if (session == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Room not found"));
        }

        return Task.FromResult(new RoomInfo
        {
            RoomId = session.RoomId,
            Phase = session.CurrentPhase.ToString(),
            CurrentRound = session.CurrentRound,
            PlayerCount = 2
        });
    }

    public override Task<ListActiveRoomsResponse> ListActiveRooms(
        ListActiveRoomsRequest request, ServerCallContext context)
    {
        var sessions = _sessionRegistry.GetActiveSessions();
        var maxResults = request.MaxResults > 0 ? request.MaxResults : 10;

        var rooms = sessions
            .Take(maxResults)
            .Select(s => new RoomInfo
            {
                RoomId = s.RoomId,
                Phase = s.CurrentPhase.ToString(),
                CurrentRound = s.CurrentRound,
                PlayerCount = 2
            })
            .ToList();

        return Task.FromResult(new ListActiveRoomsResponse
        {
            Rooms = { rooms }
        });
    }
}