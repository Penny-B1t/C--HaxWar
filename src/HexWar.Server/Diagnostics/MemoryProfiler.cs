namespace HexWar.Server.Diagnostics;

using System.Diagnostics;
using HexWar.Application.Sessions;
using HexWar.Infrastructure.WebSocket;

/// <summary>
/// 서버 리소스 사용량을 주기적으로 측정하고 기록합니다.
/// </summary>
public class MemoryProfiler : BackgroundService
{
    private readonly SessionRegistry _sessionRegistry;
    private readonly ConnectionManager _connectionManager;
    private readonly ILogger<MemoryProfiler> _logger;

    // 측정 간격
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public MemoryProfiler(
        SessionRegistry sessionRegistry,
        ConnectionManager connectionManager,
        ILogger<MemoryProfiler> logger)
    {
        _sessionRegistry = sessionRegistry;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    // 측정 프로세스 백그라운드 실행 
    // CancellationToken은 협력적으로 동작하여야한다. 
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Memory profiler started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProfileAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during memory profiling");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private Task ProfileAsync()
    {
        var process = Process.GetCurrentProcess();
        var sessions = _sessionRegistry.GetActiveSessions();

        var stats = new ServerStats
        {
            Timestamp = DateTime.UtcNow,

            // 프로세스 메모리
            WorkingSetMB = process.WorkingSet64 / 1024.0 / 1024.0,
            PrivateMemoryMB = process.PrivateMemorySize64 / 1024.0 / 1024.0,

            // GC 메모리
            GCHeapMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
            GCGen0 = GC.CollectionCount(0),
            GCGen1 = GC.CollectionCount(1),
            GCGen2 = GC.CollectionCount(2),

            // 세션 정보
            TotalSessions = sessions.Count,
            ActiveSessions = sessions.Count(s => s.CurrentPhase == Domain.Enums.GamePhase.Planning),
            GameOverSessions = sessions.Count(s => s.CurrentPhase == Domain.Enums.GamePhase.GameOver),

            // 연결 정보
            TotalConnections = _connectionManager.GetTotalConnectionCount(),

            // 평균 세션당 메모리 (추정)
            EstimatedMemoryPerSessionKB = sessions.Any()
                ? (GC.GetTotalMemory(false) / 1024.0) / sessions.Count
                : 0,
        };

        _logger.LogInformation(
            "Memory: {WorkingSetMB:F1}MB WS, {GCMemMB:F1}MB GC | " +
            "Sessions: {Total} total, {Active} active, {GameOver} over | " +
            "Connections: {Conns} | " +
            "GC: Gen0={Gen0} Gen1={Gen1} Gen2={Gen2} | " +
            "Est/ Session: {EstKB:F1}KB",
            stats.WorkingSetMB, stats.GCHeapMB,
            stats.TotalSessions, stats.ActiveSessions, stats.GameOverSessions,
            stats.TotalConnections,
            stats.GCGen0, stats.GCGen1, stats.GCGen2,
            stats.EstimatedMemoryPerSessionKB);

        return Task.CompletedTask;
    }
}

public class ServerStats
{
    public DateTime Timestamp { get; set; }
    public double WorkingSetMB { get; set; }
    public double PrivateMemoryMB { get; set; }
    public double GCHeapMB { get; set; }
    public int GCGen0 { get; set; }
    public int GCGen1 { get; set; }
    public int GCGen2 { get; set; }
    public int TotalSessions { get; set; }
    public int ActiveSessions { get; set; }
    public int GameOverSessions { get; set; }
    public int TotalConnections { get; set; }
    public double EstimatedMemoryPerSessionKB { get; set; }
}