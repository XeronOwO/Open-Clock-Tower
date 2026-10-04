using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 复盘的可见性闸（R-0043 / D-0020）：真宿主 + 真 SignalR 客户端。
/// 进行中：说书人实时面可读、玩家面显式拒绝且留审计；结束批次之后：玩家面开放、按序号分页。
/// </summary>
public sealed class ReplayHostTests
{
    /// <summary>结束前玩家零复盘数据（反方向断言）；结束后同一连接立刻可读。</summary>
    [Fact]
    public async Task Replay_IsDeniedForPlayersBeforeEnd_AndOpensAfterEnd()
    {
        await using var host = new TestServerHost(seatCount: 4, autoStartTestNight: false);

        var assigned = await host.ExecuteHostCommandAsync(
            new AssignCharactersCommand
            {
                Assignments =
                [
                    new SeatCharacterAssignment { Seat = new SeatId(1), Character = new CharacterId("vortox") },
                    new SeatCharacterAssignment { Seat = new SeatId(2), Character = new CharacterId("clockmaker") },
                    new SeatCharacterAssignment { Seat = new SeatId(3), Character = new CharacterId("dreamer") },
                    new SeatCharacterAssignment { Seat = new SeatId(4), Character = new CharacterId("mutant") },
                ],
            },
            "test-replay-assign",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, assigned.Kind);

        var phase = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(4) },
            "test-replay-phase",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, phase.Kind);

        var storyteller = await host.ConnectStorytellerAsync();
        var player = await host.ConnectSeatAsync(new SeatId(2));

        // 说书人实时面：进行中即可读（R-0043 第 3 条），同一份投影、无第二账本。
        var live = await storyteller.InvokeAsync<ReplayViewDto>("GetReplay", 0L, 100);
        Assert.False(live.Ended);
        Assert.NotEmpty(live.Steps);

        // 玩家面：结束批次之前显式拒绝，且拒绝有审计（R-0043 第 1 条）。
        var denied = await Assert.ThrowsAsync<HubException>(
            () => player.InvokeAsync<ReplayViewDto>("GetReplay", 0L, 100));
        Assert.Contains("复盘", denied.Message);
        Assert.True(
            host.Logs.Any(line => line.Contains("复盘读取被拒", StringComparison.Ordinal)),
            "玩家侧的复盘拒绝必须有 Application 审计日志");

        // 结束批次：唯一的恶魔死亡 → 善良获胜（R-0029 的常规条件），随后一切命令被拒（R-0024）。
        var killed = await host.ExecuteHostCommandAsync(
            new ApplySeatStateCommand
            {
                Seat = new SeatId(1),
                Life = LifeState.Dead,
                Reason = "测试：恶魔死亡",
            },
            "test-replay-kill",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, killed.Kind);

        // 结束后：玩家面开放；步骤按事件序号严格递增（顺序保真、不跳步、不合并）。
        var replay = await player.InvokeAsync<ReplayViewDto>("GetReplay", 0L, 500);
        Assert.True(replay.Ended);
        Assert.NotEmpty(replay.Steps);

        var sequences = replay.Steps.Select(step => step.Sequence).ToArray();
        Assert.Equal(sequences.OrderBy(value => value).ToArray(), sequences);

        // 分页：afterSequence 推进后取到的是更晚的步骤，不重不漏。
        var first = await player.InvokeAsync<ReplayViewDto>("GetReplay", 0L, 2);
        Assert.True(first.Steps.Length <= 2);
        if (first.HasMore)
        {
            var second = await player.InvokeAsync<ReplayViewDto>(
                "GetReplay",
                first.Steps[^1].Sequence,
                2);
            Assert.NotEmpty(second.Steps);
            Assert.True(second.Steps[0].Sequence > first.Steps[^1].Sequence);
        }
    }
}
