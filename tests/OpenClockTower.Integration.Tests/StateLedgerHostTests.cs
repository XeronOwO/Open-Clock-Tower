using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 状态账在真实宿主里的行为：说书人报上来的维度进账、重启后仍在、玩家侧拿不到。
/// </summary>
/// <remarks>
/// 依据 D-0009（事件是唯一事实来源、视角投影在服务端强制）与 D-0012 §4.3（信息只在**下发方向**校验）。
/// 这里跑的是真宿主 + 真 Hub 协议 + 真 SQLite，不是直接调领域方法。
/// </remarks>
public sealed class StateLedgerHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>说书人上报的六个维度进状态账，逐维度保留归因；玩家侧的重连包里没有它。</summary>
    [Fact]
    public async Task ReportedDimensions_EnterLedgerWithAttribution_AndStayOutOfPlayerPayloads()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await using var seatOne = await host.ConnectSeatAsync(new SeatId(1));

        var result = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            3,
            "Alive",
            "clockmaker",
            "Good",
            "Sober",
            "Poisoned",
            "测试：3 号被 1 号的投毒者下毒",
            1,
            "test-ledger-attribution-1");

        Assert.Equal("Accepted", result.Kind);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var seat = Assert.Single(view.Seats, entry => entry.Seat == 3);
        Assert.Equal(5, seat.Facts.Length);

        var poison = Assert.Single(seat.Facts, fact => fact.Dimension == "Poison");
        Assert.Equal("Poisoned", poison.Value);
        Assert.Equal("测试：3 号被 1 号的投毒者下毒", poison.Reason);
        Assert.Equal(1, poison.CausedBy);

        var life = Assert.Single(seat.Facts, fact => fact.Dimension == "Life");
        Assert.Equal("Alive", life.Value);

        // 反方向：状态账是说书人视角的，玩家侧的重连包里根本不该出现它。
        var bundleJson = JsonSerializer.Serialize(host.Bundles[new SeatId(1)]);
        Assert.DoesNotContain("Poison", bundleJson);
        Assert.DoesNotContain("Facts", bundleJson);
        Assert.DoesNotContain("CausedBy", bundleJson);
    }

    /// <summary>宿主重启后（事件流重放）状态账必须还在——否则说明它只活在内存里。</summary>
    [Fact]
    public async Task Ledger_SurvivesHostRestart_ByReplayingTheEventStream()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-ledger-{Guid.NewGuid():N}.db");

        await using (var first = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            deleteDatabaseOnDispose: false))
        {
            await using var storyteller = await first.ConnectStorytellerAsync();
            var result = await storyteller.InvokeAsync<CommandResultDto>(
                "ReportSeatState",
                2,
                null,
                null,
                null,
                "Drunk",
                null,
                "测试：2 号被灌醉",
                1,
                "test-ledger-restart-1");

            Assert.Equal("Accepted", result.Kind);
        }

        // 同一个库文件再起一个宿主：恢复 = 重放事件流（D-0010）
        await using var restarted = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            deleteDatabaseOnDispose: true);

        await using var storytellerAfterRestart = await restarted.ConnectStorytellerAsync();
        var view = await TestServerHost.WaitForViewAsync(
            storytellerAfterRestart,
            candidate => candidate.Seats.Any(entry => entry.Seat == 2),
            Wait);

        Assert.NotNull(view);
        var seat = Assert.Single(view.Seats, entry => entry.Seat == 2);
        var drunk = Assert.Single(seat.Facts, fact => fact.Dimension == "Drunk");
        Assert.Equal("Drunk", drunk.Value);
        Assert.Equal("测试：2 号被灌醉", drunk.Reason);
        Assert.Equal(1, drunk.CausedBy);
    }
}
