using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 结束态在步骤机里的折叠与闸门（R-0024 第 4 条）：一局只结束一次；结束后一切输入被拒。
/// </summary>
public sealed class StepMachineGameEndTests
{
    [Fact]
    public void GameEnded_FoldsOutcome_RejectsInputs_AndRefusesASecondEnd()
    {
        var started = StepMachine.StartPhase(StepFixture.Plan("test:day-1", StepFixture.Empty("empty-1")));
        var ended = StepMachine.Apply(started.State, Ended())!;

        Assert.NotNull(ended.Outcome);
        Assert.Equal(Alignment.Evil, ended.Outcome!.Winner);
        Assert.Equal(OutcomeCondition.VortoxNoExecution, ended.Outcome.Condition);

        // 结束后一切输入被拒：连说书人的接管也不例外（撤销只能走截断重放，D-0010）。
        var rejected = StepMachine.Handle(ended, new TakeOverInput { Reason = "测试：结束后接管" });
        Assert.Equal(StepMachineOutcomeKind.Rejected, rejected.Kind);
        Assert.Equal(StepMachineRejectionReason.GameEnded, rejected.RejectionReason);

        // 一局只能结束一次：重复的结束事件是事件流损坏，必须显式失败。
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(ended, Ended()));
    }

    /// <summary>
    /// 呆瓜选择折叠成账目；同一名呆瓜至多两条（咖啡师「行动两次」会让他选两次，R-0052 第 3 条：
    /// 触发层按窗口判定"选几次"，折叠层只守住"不会更多"的硬上限，R-0027 的幂等依据仍然成立）。
    /// </summary>
    [Fact]
    public void KlutzChoice_FoldsUpToTwo_AndRefusesMore()
    {
        var started = StepMachine.StartPhase(StepFixture.Plan("test:day-1", StepFixture.Empty("empty-1")));
        var first = new KlutzChoiceMadeEvent { Klutz = new SeatId(1), Target = new SeatId(2) };
        var second = new KlutzChoiceMadeEvent { Klutz = new SeatId(1), Target = new SeatId(3) };

        var afterFirst = StepMachine.Apply(started.State, first)!;

        var record = Assert.Single(afterFirst.KlutzChoices);
        Assert.Equal(new SeatId(1), record.Klutz);
        Assert.Equal(new SeatId(2), record.Target);
        Assert.True(record.IsMade);

        // 第二次选择在「行动两次」窗口下合法；第三次就是事件流损坏。
        var afterSecond = StepMachine.Apply(afterFirst, second)!;
        Assert.Equal(2, afterSecond.KlutzChoices.Count);
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(afterSecond, first));
    }

    private static GameEndedEvent Ended() =>
        new()
        {
            Winner = Alignment.Evil,
            Condition = OutcomeCondition.VortoxNoExecution,
            Detail = "测试：涡流黄昏无人被处决",
        };
}
