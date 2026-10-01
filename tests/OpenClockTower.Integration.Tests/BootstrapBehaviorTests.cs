using OpenClockTower.Application;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 引导行为：宿主不再自动开阶段、席位数按配置播种——两者都要有运行时证据。
/// </summary>
/// <remarks>
/// 依据 docs/architecture/current.md §2.6：夜晚计划由结算引擎按 Rules 顺序表与在场角色构建，
/// 引导阶段不伪造计划。旧的"启动即开演示阶段"已随占位计划一并移除；本用例同时断言
/// "不开阶段"不是"开不了阶段"——显式开阶段仍然成功。
/// </remarks>
public sealed class BootstrapBehaviorTests
{
    /// <summary>新库启动：不自动开阶段；席位数量按配置播种；显式开阶段仍然可用。</summary>
    [Fact]
    public async Task HostStart_DoesNotAutoStartNight_ButAcceptsExplicitStart()
    {
        const int seatCount = 4; // 与默认值 5 区分，确保断言真的在验证配置
        await using var host = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: seatCount,
            autoStartTestNight: false);

        Assert.Null(host.Session.GetStorytellerView().Phase);

        var setup = await host.GetSetupAsync();
        Assert.Equal(seatCount, setup.Seats.Count);

        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
            "test-bootstrap-explicit-start",
            CancellationToken.None);

        Assert.Equal(CommandResultKind.Accepted, started.Kind);
        Assert.NotNull(host.Session.GetStorytellerView().Phase);
    }
}
