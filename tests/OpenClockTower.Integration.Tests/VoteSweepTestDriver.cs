using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 集成用例里的钟盘收票驱动：把「说书人开始 + 控制面逐席到点」两步显式跑出来（R-0017 目标形态）。
/// </summary>
/// <remarks>
/// 真宿主的节拍器按服务端时钟自动收票；集成用例要逐步断言「开始 → 举手 → 收票 → 计票」，
/// 所以这里用**远超用例时长**的参数（10s / 5s）把自动节拍挡在用例之外，再以系统身份手动送
/// 逐席输入。走的仍是生产同一套命令与内核判定（不是测试旁路）。
/// </remarks>
internal static class VoteSweepTestDriver
{
    internal const int SlowCountdownMilliseconds = 10_000;
    internal const int SlowIntervalMilliseconds = 5_000;

    /// <summary>说书人 / 宿主开始收票（慢节奏，避免真节拍器在用例中途插手）。</summary>
    internal static async Task<CommandResult> StartAsync(TestServerHost host, int nominationIndex, string key)
    {
        var result = await host.ExecuteHostCommandAsync(
            new StartVoteSweepCommand
            {
                NominationIndex = nominationIndex,
                CountdownMilliseconds = SlowCountdownMilliseconds,
                IntervalMilliseconds = SlowIntervalMilliseconds,
            },
            key,
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, result.Kind);
        return result;
    }

    /// <summary>按座次把一圈收完（系统身份，模拟控制面到点）。</summary>
    internal static async Task CollectAllAsync(
        TestServerHost host,
        int nominationIndex,
        int seatCount,
        string keyPrefix)
    {
        for (var seat = 1; seat <= seatCount; seat++)
        {
            var result = await host.ExecuteSystemCommandAsync(
                new CollectSeatVoteCommand
                {
                    NominationIndex = nominationIndex,
                    Seat = new SeatId(seat),
                },
                $"{keyPrefix}:seat-{seat}",
                CancellationToken.None);
            Assert.Equal(CommandResultKind.Accepted, result.Kind);
        }
    }

    /// <summary>继续中断的收票（重启恢复后由说书人显式继续）。</summary>
    internal static async Task ResumeAsync(TestServerHost host, int nominationIndex, string key)
    {
        var result = await host.ExecuteHostCommandAsync(
            new ResumeVoteSweepCommand { NominationIndex = nominationIndex },
            key,
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, result.Kind);
    }

    /// <summary>开始 + 收完一圈（最常用的一步到位）。</summary>
    internal static async Task RunAsync(
        TestServerHost host,
        int nominationIndex,
        int seatCount,
        string keyPrefix)
    {
        await StartAsync(host, nominationIndex, $"{keyPrefix}:start");
        await CollectAllAsync(host, nominationIndex, seatCount, keyPrefix);
    }
}
