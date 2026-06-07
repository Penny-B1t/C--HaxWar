namespace HexWar.Matchmaking.Services;

using System.Collections.Concurrent;
using Grpc.Core;
using HexWar.Application.Sessions;
using HexWar.Matchmaking;
using Microsoft.Extensions.Logging;

/// <summary>
/// gRPC 매치메이킹 서비스 구현체
/// </summary>
public class MatchmakingService : HexWar.Matchmaking.MatchmakingService.MatchmakingServiceBase
{
    private readonly MatchmakingQueue _queue;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ILogger<MatchmakingService> _logger;
    
    // 매칭 완료된 플레이어에게 결과를 전달하기 위한 채널과 비동기 완료 알림용 TaskCompletionSource
    private readonly ConcurrentDictionary<string, (IServerStreamWriter<MatchmakingUpdate> Stream, TaskCompletionSource<MatchmakingUpdate> Completion)> _waitingPlayers = new();

    public MatchmakingService(
        MatchmakingQueue queue,
        SessionRegistry sessionRegistry,
        ILogger<MatchmakingService> logger)
    {
        _queue = queue;
        _sessionRegistry = sessionRegistry;
        _logger = logger;
        
        // 매칭 완료 이벤트 구독
        _queue.OnMatchFound += OnMatchFound;
    }

    /// <summary>
    /// 매칭 큐 참가 (서버 스트리밍)
    /// </summary>
    public override async Task JoinQueue(
        JoinQueueRequest request,
        IServerStreamWriter<MatchmakingUpdate> responseStream,
        ServerCallContext context)
    {
        var playerId = request.PlayerId;
        _logger.LogInformation("Player {PlayerId} joined matchmaking queue", playerId);

        // 이미 큐에 있는지 확인
        if (_waitingPlayers.ContainsKey(playerId))
        {
            await responseStream.WriteAsync(new MatchmakingUpdate
            {
                Status = MatchmakingStatus.Error
            });
            return;
        }

        // 응답 스트림 및 완료 알림 등록
        var matchCompletion = new TaskCompletionSource<MatchmakingUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waitingPlayers[playerId] = (responseStream, matchCompletion);

        // 큐에 등록
        var queuedPlayer = _queue.Enqueue(playerId, request.Rating, context.CancellationToken);

        try
        {
            // 매칭될 때까지 상태 업데이트 전송
            while (!context.CancellationToken.IsCancellationRequested)
            {
                if (matchCompletion.Task.IsCompleted)
                {
                    break;
                }

                var status = _queue.GetStatus(playerId);
                
                if (status.IsInQueue)
                {
                    await responseStream.WriteAsync(new MatchmakingUpdate
                    {
                        Status = MatchmakingStatus.Searching,
                        QueuePosition = status.Position,
                        EstimatedWaitSeconds = status.EstimatedWaitSeconds
                    });
                }
                else
                {
                    // 큐에 없고 매칭도 완료되지 않았다면 매칭 처리 중이므로 잠시 대기
                    var completedTask = await Task.WhenAny(matchCompletion.Task, Task.Delay(3000, context.CancellationToken));
                    if (completedTask == matchCompletion.Task)
                    {
                        break;
                    }
                    else
                    {
                        // 3초 이내에 매칭 정보가 안 온다면 비정상 종료로 판단
                        break;
                    }
                }

                await Task.WhenAny(matchCompletion.Task, Task.Delay(1000, context.CancellationToken));
            }

            if (matchCompletion.Task.IsCompleted)
            {
                var finalUpdate = await matchCompletion.Task;
                await responseStream.WriteAsync(finalUpdate);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Player {PlayerId} cancelled matchmaking", playerId);
            _queue.Dequeue(playerId);
            await responseStream.WriteAsync(new MatchmakingUpdate
            {
                Status = MatchmakingStatus.Cancelled
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during matchmaking for player {PlayerId}", playerId);
            await responseStream.WriteAsync(new MatchmakingUpdate
            {
                Status = MatchmakingStatus.Error
            });
        }
        finally
        {
            _waitingPlayers.TryRemove(playerId, out _);
            _queue.Dequeue(playerId);
        }
    }

    /// <summary>
    /// 매칭 완료 처리
    /// </summary>
    private async void OnMatchFound(object? sender, MatchFoundEventArgs e)
    {
        var roomId = Guid.NewGuid().ToString("N")[..8]; // 8자리 짧은 ID
        
        try
        {
            // 게임 세션 생성
            var session = await _sessionRegistry.CreateSessionAsync(roomId);
            var gameRoom = session.GetGameRoom();
            
            // 플레이어 등록
            gameRoom.AddPlayer(new Domain.ValueObjects.PlayerId(e.Player1.PlayerId));
            gameRoom.AddPlayer(new Domain.ValueObjects.PlayerId(e.Player2.PlayerId));

            _logger.LogInformation("Match found: {RoomId}, Player1={P1}, Player2={P2}", 
                roomId, e.Player1.PlayerId, e.Player2.PlayerId);

            // WebSocket 엔드포인트 (실제 포트인 5183 사용)
            var wsEndpoint = $"ws://localhost:5183/ws/game/{roomId}";

            // Player 1에게 매칭 결과 전송
            if (_waitingPlayers.TryGetValue(e.Player1.PlayerId, out var state1))
            {
                state1.Completion.TrySetResult(new MatchmakingUpdate
                {
                    Status = MatchmakingStatus.Matched,
                    MatchResult = new MatchFoundResult
                    {
                        RoomId = roomId,
                        PlayerSide = "A",
                        WsEndpoint = wsEndpoint,
                        OpponentId = e.Player2.PlayerId
                    }
                });
            }

            // Player 2에게 매칭 결과 전송
            if (_waitingPlayers.TryGetValue(e.Player2.PlayerId, out var state2))
            {
                state2.Completion.TrySetResult(new MatchmakingUpdate
                {
                    Status = MatchmakingStatus.Matched,
                    MatchResult = new MatchFoundResult
                    {
                        RoomId = roomId,
                        PlayerSide = "B",
                        WsEndpoint = wsEndpoint,
                        OpponentId = e.Player1.PlayerId
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating match for room {RoomId}", roomId);
            // 예외 발생 시 에러 알림 전송하여 클라이언트의 비한정 루프 탈출
            if (_waitingPlayers.TryGetValue(e.Player1.PlayerId, out var state1))
            {
                state1.Completion.TrySetResult(new MatchmakingUpdate { Status = MatchmakingStatus.Error });
            }
            if (_waitingPlayers.TryGetValue(e.Player2.PlayerId, out var state2))
            {
                state2.Completion.TrySetResult(new MatchmakingUpdate { Status = MatchmakingStatus.Error });
            }
        }
    }

    /// <summary>
    /// 큐에서 나가기
    /// </summary>
    public override Task<LeaveQueueResponse> LeaveQueue(
        LeaveQueueRequest request, ServerCallContext context)
    {
        var removed = _queue.Dequeue(request.PlayerId);
        
        return Task.FromResult(new LeaveQueueResponse
        {
            Success = removed
        });
    }

    /// <summary>
    /// 큐 상태 확인
    /// </summary>
    public override Task<QueueStatus> GetQueueStatus(
        GetQueueStatusRequest request, ServerCallContext context)
    {
        var status = _queue.GetStatus(request.PlayerId);
        
        return Task.FromResult(new QueueStatus
        {
            IsInQueue = status.IsInQueue,
            QueuePosition = status.Position,
            EstimatedWaitSeconds = status.EstimatedWaitSeconds,
            PlayersInQueue = status.TotalInQueue
        });
    }
}