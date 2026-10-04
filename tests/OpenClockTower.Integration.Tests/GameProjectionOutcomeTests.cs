using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 结束面与呆瓜记录的**下发裁剪**（D-0012 §4.3）：独立对抗性复核 F-2 / F-3 的回归。
/// </summary>
/// <remarks>
/// 纯投影用例：不建宿主，直接给投影喂步骤机状态与账，断言"该看见的看见了、不该看见的没看见"。
/// </remarks>
public sealed class GameProjectionOutcomeTests
{
    /// <summary>跳过记录的原因（醉酒 / 中毒 / 被作废）是说书人专属 → 玩家面只说"没做出选择"。</summary>
    [Fact]
    public void SkippedKlutzChoice_PlayerView_HidesTheReason()
    {
        const string storytellerOnlyReason = "呆瓜死亡时能力未生效（醉酒 / 中毒，或这两维之一尚未观测）：不进行死亡选择";
        var machine = Machine() with
        {
            KlutzChoices =
            [
                new KlutzChoiceRecord
                {
                    Klutz = new SeatId(2),
                    Target = null,
                    Detail = storytellerOnlyReason,
                },
            ],
        };

        var view = Project(machine, new SeatId(1));
        var wire = ProjectionMapper.ToDto(view);

        var record = Assert.Single(wire.KlutzChoices);
        Assert.Null(record.Target);
        Assert.DoesNotContain("醉酒", record.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("中毒", record.Detail, StringComparison.Ordinal);
        Assert.Contains("没有做出选择", record.Detail, StringComparison.Ordinal);

        // 说书人视图保留完整原因（两本账：玩家面裁剪，说书人面不裁剪）。
        var storyteller = GameProjection.ForStoryteller(
            machine,
            new GameState(),
            RoomHealth.Healthy,
            sequence: 7,
            pendingSince: null,
            now: DateTimeOffset.UnixEpoch,
            voteSweepStartedAt: null,
            recentSeatChanges: [],
            annotations: [],
            seatNames: []);
        Assert.Contains("醉酒", Assert.Single(storyteller.KlutzChoices).Detail, StringComparison.Ordinal);
    }

    /// <summary>结束态不再下发任何请求：终局快照里残留的请求对玩家是死信（提交必被拒）。</summary>
    [Fact]
    public void EndedGame_PlayerView_DoesNotPushAPendingRequest()
    {
        var machine = Machine() with
        {
            PendingRequest = new OperationRequest
            {
                Id = new OperationRequestId("klutz:2"),
                Addressee = new SeatId(2),
                Origin = OperationRequestOrigin.ForTrigger(new AbilityId("klutz.choice"), "测试：呆瓜死亡选择"),
                Prompt = new ChoicePrompt
                {
                    Context = "测试：呆瓜选择",
                    Options = [new DecisionOption { Value = "seat:1", Preview = "1 号玩家" }],
                    OnNoOption = NoOptionBehavior.BlockAndAlert,
                },
            },
            Outcome = new GameOutcome
            {
                Winner = Alignment.Good,
                Condition = OutcomeCondition.DemonsAllDead,
                Detail = "测试：恶魔全死",
            },
        };

        var toKlutz = Project(machine, new SeatId(2));
        Assert.Null(toKlutz.PendingRequest);

        // 结束前同一条请求是下发的（对照：不是"投影从来不给请求"，而是"结束后不给"）。
        var open = machine with { Outcome = null };
        Assert.NotNull(Project(open, new SeatId(2)).PendingRequest);
    }

    private static PlayerView Project(StepMachineState machine, SeatId seat) =>
        GameProjection.ForSeat(
            machine,
            new GameState(),
            [new SeatId(1), new SeatId(2)],
            sequence: 7,
            now: DateTimeOffset.UnixEpoch,
            voteSweepStartedAt: null,
            seat,
            new SessionTrackers(),
            seatNames: []);

    private static StepMachineState Machine(params KlutzChoiceRecord[] choices) =>
        new()
        {
            Plan = new StepPlan
            {
                Label = "test:day-1",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day"))],
            },
            SlotIndex = 0,
            Quota = SlotQuotaState.Running,
            Control = ControlMode.Automatic,
            KlutzChoices = choices,
        };
}
