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

    /// <summary>呆瓜选择折叠成账目；同一名呆瓜只能有一条记录（幂等依据，R-0027）。</summary>
    [Fact]
    public void KlutzChoice_FoldsOnce_AndRefusesDuplicates()
    {
        var started = StepMachine.StartPhase(StepFixture.Plan("test:day-1", StepFixture.Empty("empty-1")));
        var choice = new KlutzChoiceMadeEvent { Klutz = new SeatId(1), Target = new SeatId(2) };

        var folded = StepMachine.Apply(started.State, choice)!;

        var record = Assert.Single(folded.KlutzChoices);
        Assert.Equal(new SeatId(1), record.Klutz);
        Assert.Equal(new SeatId(2), record.Target);
        Assert.True(record.IsMade);

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(folded, choice));
    }

    private static GameEndedEvent Ended() =>
        new()
        {
            Winner = Alignment.Evil,
            Condition = OutcomeCondition.VortoxNoExecution,
            Detail = "测试：涡流黄昏无人被处决",
        };
}
