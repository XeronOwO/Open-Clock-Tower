using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 开局设置阶段的座位观测：说书人必须能在**首个阶段开始之前**把观测到的维度写进状态账。
/// </summary>
/// <remarks>
/// <para>
/// 2026-10-02 真机验证时暴露：<c>ApplySeatStateCommand</c> 被阶段闸以 <c>phase.not_started</c>
/// 挡在首个阶段之前，而状态账（D-0015）本来就是"观测即记账"的载体、开局分配走的正是这条
/// 预阶段通路（D-0017）。修法：阶段闸与身份闸为它单开一条预阶段通路，事件形状与运行期一致。
/// </para>
/// <para>
/// 口径说明（同一次验证的另一条发现）：建表要求**每一席都有角色**（<c>plan.seat_unassigned</c>），
/// "只分配一半"不是合法开局；因此本文件不把"补报缺角色席位"写成可开夜路径，
/// 只断言预阶段观测本身可用、可归因。
/// </para>
/// </remarks>
public sealed class PrePhaseSeatObservationTests
{
    /// <summary>首个阶段之前可以上报状态（只写账、不开阶段），并且带上原因与归因。</summary>
    [Fact]
    public async Task SeatObservationBeforeFirstPhase_IsAccepted_AndDoesNotStartAPhase()
    {
        await using var host = new TestServerHost(autoStartTestNight: false);

        var result = await host.ExecuteHostCommandAsync(
            new ApplySeatStateCommand
            {
                Seat = new SeatId(2),
                Life = LifeState.Dead,
                Poison = PoisonState.Poisoned,
                Reason = "开局前说书人裁定",
                CausedBy = new SeatId(1),
            },
            "test-prepbase-report-1",
            CancellationToken.None);

        Assert.True(
            result.Kind == CommandResultKind.Accepted,
            result.Rejection is null ? "无拒绝信息" : $"{result.Rejection.Code}: {result.Rejection.Message}");

        // 只写账、不开阶段：阶段仍为空，观察到的两个维度各自带原因与归因。
        var view = host.Session.GetStorytellerView();
        Assert.Null(view.Phase);
        var seat = Assert.Single(view.Seats, entry => entry.Seat == new SeatId(2));
        Assert.Equal(LifeState.Dead, seat.LifeValue);
        Assert.Equal(PoisonState.Poisoned, seat.PoisonValue);
        Assert.Equal("开局前说书人裁定", seat.Life!.Reason);
        Assert.Equal(new SeatId(1), seat.Life.CausedBy);
    }

    /// <summary>合法的开局（每席都有角色）在预阶段观测之后仍能正常开夜——观测不会污染建表输入。</summary>
    [Fact]
    public async Task ObservationsBeforeFirstPhase_ThenRealRulesNightStarts()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 3);

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
            "test-prepbase-assign",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, assigned.Kind);

        var observed = await host.ExecuteHostCommandAsync(
            new ApplySeatStateCommand
            {
                Seat = new SeatId(2),
                Drunk = DrunkState.Drunk,
                Reason = "开局前观测：已醉酒",
            },
            "test-prepbase-report-2",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, observed.Kind);

        var night = await host.ExecuteHostCommandAsync(
            new StartNightCommand { NightNumber = 1, Variant = NightOrderVariant.Recommended },
            "test-prepbase-start-night",
            CancellationToken.None);

        Assert.True(
            night.Kind == CommandResultKind.Accepted,
            night.Rejection is null ? "无拒绝信息" : $"{night.Rejection.Code}: {night.Rejection.Message}");
        var view = host.Session.GetStorytellerView();
        Assert.Equal(GamePhase.FirstNight, view.Phase);
        Assert.True(view.SlotCount > 0);
        Assert.Equal(DrunkState.Drunk, Assert.Single(view.Seats, entry => entry.Seat == new SeatId(2)).DrunkValue);
    }
}
