using BenchmarkDotNet.Running;

namespace HexWar.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<GameRoomBenchmarks>();
    }
}
