namespace HexWar.Matchmaking.Services;

using System.Collections.Concurrent;
using Grpc.Core;

public class MatchmakingQueue
{
    // 참조형 자료형을 readonly로 사용할 경우 객체 자체는 불변하지만, 내부 필드는 변경 가능하기 때문에 
    private readonly ConcurrentQueue<QueuedPlayer> _queue = new();
    private readonly object _matchLock = new();

    // 매칭 이벤트 핸들러
    public event EventHandler<MatchFoundEventArgs> OnMatchFound;

    public QueuedPlayer Enqueue(string playerId, int rating, CancellationToken cancellationToken)
    {
        var player = new QueuedPlayer
        {
            PlayerId = playerId,
            Rating = rating,
            JoinedAt = DateTime.UtcNow,
            CancellationToken = cancellationToken
        };

        _queue.Enqueue(player);

        TryMatch();

        return player;
    }

    public bool Dequeue(string playerId)
    {
        // 삭제 대상 유저를 제외한 리스트 생성
        var remaining = _queue.Where(p => p.PlayerId != playerId).ToList();

        while (_queue.TryDequeue(out _)) { } // 기존 큐 비우기

        foreach (var player in remaining)
        {
            _queue.Enqueue(player);
        }

        return remaining.Count < _queue.Count + 1; // 하나 제거되었는지 확인
    }

    private void TryMatch()
    {
        lock (_matchLock)
        {
            if (_queue.Count < 2) return;

            // 두 명 꺼내기
            if (_queue.TryDequeue(out var player1) && _queue.TryDequeue(out var player2))
            {
                // 취소된 플레이어 건너뛰기
                if (player1.CancellationToken.IsCancellationRequested)
                {
                    if (!player2.CancellationToken.IsCancellationRequested)
                        _queue.Enqueue(player2);
                    TryMatch(); // 재시도
                    return;
                }

                if (player2.CancellationToken.IsCancellationRequested)
                {
                    _queue.Enqueue(player1);
                    TryMatch(); // 재시도
                    return;
                }

                // 매칭 성공
                OnMatchFound?.Invoke(this, new MatchFoundEventArgs(player1, player2));
            }
        }
    }

    // 현재 대기열 내에 상대적 정보 추출
    public QueueStatusInfo GetStatus(string playerId)
    {
        var players = _queue.ToList();
        var player = players.FirstOrDefault(p => p.PlayerId == playerId);

        return new QueueStatusInfo
        {
            IsInQueue = player != null,
            Position = player != null ? players.IndexOf(player) + 1 : 0,
            TotalInQueue = players.Count,
            EstimatedWaitSeconds = players.Count * 5 // 1인당 5초 예상
        };
    }

}

public class QueuedPlayer
{
    public string PlayerId { get; init; } = string.Empty;
    public int Rating { get; init; }
    public DateTime JoinedAt { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public class MatchFoundEventArgs : EventArgs
{
    public QueuedPlayer Player1 { get; }
    public QueuedPlayer Player2 { get; }

    public MatchFoundEventArgs(QueuedPlayer player1, QueuedPlayer player2)
    {
        Player1 = player1;
        Player2 = player2;
    }
}

public class QueueStatusInfo
{
    public bool IsInQueue { get; init; }
    public int Position { get; init; }
    public int TotalInQueue { get; init; }
    public int EstimatedWaitSeconds { get; init; }
}