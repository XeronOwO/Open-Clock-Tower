using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 命令层夜晚口径默认值（R-0014）：无参开夜必须落到官方魔典顺序（<c>Recommended</c>），
/// 显式口径仍然优先——消除「文档默认 vs 无参默认」的最后一处偏差。
/// </summary>
/// <remarks>
/// 无参构造路径（宿主脚本 / 装置 / 集成夹具）直接走 <see cref="StartNightCommand.Variant"/> 的默认值，
/// 生产面板与 Hub 路径始终显式传口径（面板初始 <c>Recommended</c>）——两条路径都要有回归。
/// </remarks>
public sealed class StartNightVariantDefaultTests
{
    /// <summary>无参构造的命令建出的表就是推荐口径，且槽位顺序真是推荐表（不只查标签）。</summary>
    [Fact]
    public async Task StartNightWithoutExplicitVariant_UsesRecommendedTable()
    {
        await using var host = await HostWithAssignedSeatsAsync();

        var result = await host.ExecuteHostCommandAsync(
            new StartNightCommand { NightNumber = 1 },
            "test-variant-default-night",
            CancellationToken.None);

        Assert.True(
            result.Kind == CommandResultKind.Accepted,
            result.Rejection is null ? "无拒绝信息" : $"{result.Rejection.Code}: {result.Rejection.Message}");

        var plan = await StartedPlanAsync(host);
        Assert.Equal("Recommended", plan.Variant);

        // 推荐口径首夜哲学家在信息环节之前（索引 2）；原本口径在索引 4——顺序本身是行为证据。
        Assert.Equal(2, SlotIndexOf(plan, "philosopher"));
    }

    /// <summary>显式选原本口径时新默认值不得覆盖说书人的选择，且照旧记录进计划（R-0014）。</summary>
    [Fact]
    public async Task StartNightWithExplicitOriginal_StillUsesOriginalTable()
    {
        await using var host = await HostWithAssignedSeatsAsync();

        var result = await host.ExecuteHostCommandAsync(
            new StartNightCommand { NightNumber = 1, Variant = NightOrderVariant.Original },
            "test-variant-explicit-original",
            CancellationToken.None);

        Assert.True(
            result.Kind == CommandResultKind.Accepted,
            result.Rejection is null ? "无拒绝信息" : $"{result.Rejection.Code}: {result.Rejection.Message}");

        var plan = await StartedPlanAsync(host);
        Assert.Equal("Original", plan.Variant);
        Assert.Equal(4, SlotIndexOf(plan, "philosopher"));
    }

    /// <summary>三席全分配（建表要求每席都有角色）；不开测试夹具夜——首个阶段交给被测命令开。</summary>
    private static async Task<TestServerHost> HostWithAssignedSeatsAsync()
    {
        var host = new TestServerHost(seatCount: 3, autoStartTestNight: false);
        var assigned = await host.ExecuteHostCommandAsync(
            new AssignCharactersCommand
            {
                Assignments =
                [
                    new SeatCharacterAssignment { Seat = new SeatId(1), Character = new CharacterId("clockmaker") },
                    new SeatCharacterAssignment { Seat = new SeatId(2), Character = new CharacterId("dreamer") },
                    new SeatCharacterAssignment { Seat = new SeatId(3), Character = new CharacterId("no-dashii") },
                ],
            },
            "test-variant-default-assign",
            CancellationToken.None);

        Assert.True(
            assigned.Kind == CommandResultKind.Accepted,
            assigned.Rejection is null ? "无拒绝信息" : $"{assigned.Rejection.Code}: {assigned.Rejection.Message}");
        return host;
    }

    private static async Task<StepPlan> StartedPlanAsync(TestServerHost host)
    {
        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var started = Assert.Single(events.Select(stored => stored.Event).OfType<PhaseStartedEvent>());
        return started.Plan;
    }

    private static int SlotIndexOf(StepPlan plan, string character)
    {
        for (var index = 0; index < plan.Slots.Count; index++)
        {
            if (plan.Slots[index].Character is { } candidate && candidate.Value == character)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"计划里没有角色 {character} 的槽位");
    }
}
