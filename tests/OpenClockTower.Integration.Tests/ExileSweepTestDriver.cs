using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 集成用例里的流放收票驱动（与 <see cref="VoteSweepTestDriver"/> 同款）：慢节奏挡住真节拍器，
/// 再以系统身份手动逐席送「到点」输入——走的仍是生产同一套命令与内核判定（不是测试旁路）。
/// </summary>
internal static class ExileSweepTestDriver
{
    internal const int SlowCountdownMilliseconds = 10_000;
    internal const int SlowIntervalMilliseconds = 5_000;

    /// <summary>说书人 / 宿主开始流放收票（慢节奏，避免真节拍器在用例中途插手）。</summary>
    internal static async Task<CommandResult> StartAsync(TestServerHost host, int exileIndex, string key)
    {
        var result = await host.ExecuteHostCommandAsync(
            new StartExileSweepCommand
            {
                ExileIndex = exileIndex,
                CountdownMilliseconds = SlowCountdownMilliseconds,
                IntervalMilliseconds = SlowIntervalMilliseconds,
            },
            key,
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, result.Kind);
        return result;
    }

    /// <summary>按收票名册（开始时的在局座次快照）逐席收完一圈。</summary>
    internal static async Task CollectAllAsync(
        TestServerHost host,
        int exileIndex,
        IEnumerable<SeatId> seats,
        string keyPrefix)
    {
        foreach (var seat in seats)
        {
            var result = await host.ExecuteSystemCommandAsync(
                new CollectExileSeatVoteCommand
                {
                    ExileIndex = exileIndex,
                    Seat = seat,
                },
                $"{keyPrefix}:seat-{seat.Value}",
                CancellationToken.None);
            Assert.Equal(CommandResultKind.Accepted, result.Kind);
        }
    }
}
