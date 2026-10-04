using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 统一死亡保护收口（票据 `traveller-and-exile` · D3 / R-0048；处罚处决票）：
/// 流放计票、<see cref="DayMachine.CloseDay"/> 的处决收口与处罚处决（<c>AdjudicatedExecutionMachine</c>，R-0020）
/// 三条致死路径都先问同一条保护查询；待裁定 / 判定不了显式拒绝。
/// </summary>
/// <remarks>
/// 内核不认识角色：这里用脚本化的假来源覆盖四态；怪咖来源本身的判定见规则层
/// <c>DeviantProtectionSourceTests</c>。裁定受理条件与折叠损坏同样是本文件的守备面。
/// </remarks>
public sealed class DayProtectionTests
{
    [Fact]
    public void ExileCount_WithProtectionSourceReportingProtected_KeepsTheTargetAlive()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "barista", LifeState.Alive),
                (3, "barista", LifeState.Alive)),
            _ => Protected("测试来源：受保护"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var swept = ExilePhaseFixture.RunSweep(proposed.State, context, 1, 1, 2);
        var counted = ExilePhaseFixture.Count(swept, context, 1);

        Assert.Equal(StepMachineOutcomeKind.Applied, counted.Kind);
        var countedEvent = Assert.Single(counted.Events.OfType<ExileVoteCountedEvent>());
        Assert.Equal(ExileConclusion.Protected, countedEvent.Conclusion);
        Assert.Empty(counted.Events.OfType<SeatStateChangedEvent>());
    }

    [Fact]
    public void ExileCount_WithUndecidedProtection_RejectsAndKeepsTheExileOpen()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "barista", LifeState.Alive),
                (3, "barista", LifeState.Alive)),
            _ => NeedsRuling("测试来源：待裁定"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var swept = ExilePhaseFixture.RunSweep(proposed.State, context, 1, 1, 2);
        var counted = ExilePhaseFixture.Count(swept, context, 1);

        Assert.Equal(StepMachineOutcomeKind.Rejected, counted.Kind);
        Assert.Equal("day.exile_protection_required", counted.RejectionCode);
        Assert.Empty(counted.Events);

        // 收口被拒后流放仍开着：裁定完还能重新计票（不静默了结）。
        var exile = swept.Day!.OpenDay!.OpenExile!;
        Assert.Equal(ExileStatus.Voting, exile.Status);
        Assert.Null(exile.Conclusion);
    }

    [Fact]
    public void ExileCount_WithIndeterminateProtection_Rejects()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "barista", LifeState.Alive),
                (3, "barista", LifeState.Alive)),
            _ => Indeterminate("测试来源：判定不了"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var swept = ExilePhaseFixture.RunSweep(proposed.State, context, 1, 1, 2);
        var counted = ExilePhaseFixture.Count(swept, context, 1);

        Assert.Equal(StepMachineOutcomeKind.Rejected, counted.Kind);
        Assert.Equal("day.exile_protection_indeterminate", counted.RejectionCode);
    }

    [Fact]
    public void ResolveProtection_RequiresReachedExileOnThatSeat()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "barista", LifeState.Alive),
                (3, "barista", LifeState.Alive)),
            ctx => NeedsRuling("测试来源：待裁定"));

        var day = ExilePhaseFixture.StartDay();

        // 没有流放：不受理。
        Assert.Equal(
            "day.protection_not_required",
            DayPhaseFixture.CodeOf(Resolve(day, context, seat: 2, isProtected: true)));

        // 收票还没开始 / 还没收完 / 没有达线：不受理。
        var proposed = ExilePhaseFixture.Propose(day, context, proposer: 1, target: 2);
        Assert.Equal(
            "day.protection_not_required",
            DayPhaseFixture.CodeOf(Resolve(proposed.State, context, seat: 2, isProtected: true)));

        var sweptWithOneVote = ExilePhaseFixture.RunSweep(proposed.State, context, 1, 1);
        Assert.Equal(
            "day.protection_not_required",
            DayPhaseFixture.CodeOf(Resolve(sweptWithOneVote, context, seat: 2, isProtected: true)));

        // 席位不在局：拒绝码与"不该裁"分开。
        Assert.Equal(
            "day.protection_seat_unknown",
            DayPhaseFixture.CodeOf(Resolve(sweptWithOneVote, context, seat: 9, isProtected: true)));
    }

    [Fact]
    public void ResolveProtection_RecordsTheDecision_ThenTheCountUsesIt()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "barista", LifeState.Alive),
                (3, "barista", LifeState.Alive)),
            ctx => ctx.Day?.ProtectionDecisionFor(ctx.Seat) is { } decision
                ? decision.Protected
                    ? Protected("已裁定：受保护")
                    : NotProtected("已裁定：不受保护")
                : NeedsRuling("还没裁定"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var swept = ExilePhaseFixture.RunSweep(proposed.State, context, 1, 1, 2);

        var resolved = Resolve(swept, context, seat: 2, isProtected: true);
        Assert.Equal(StepMachineOutcomeKind.Applied, resolved.Kind);
        var decided = Assert.Single(resolved.Events.OfType<DayProtectionDecidedEvent>());
        Assert.Equal(new SeatId(2), decided.Seat);
        Assert.True(decided.Protected);
        Assert.NotNull(resolved.State.Day!.OpenDay!.ProtectionDecisionFor(new SeatId(2)));

        // 每席位每天至多一条：再裁一次被拒。
        Assert.Equal(
            "day.protection_already_decided",
            DayPhaseFixture.CodeOf(Resolve(resolved.State, context, seat: 2, isProtected: false)));

        // 裁定后重新计票：结论是「受保护」，不产生死亡。
        var counted = ExilePhaseFixture.Count(resolved.State, context, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, counted.Kind);
        Assert.Equal(
            ExileConclusion.Protected,
            counted.Events.OfType<ExileVoteCountedEvent>().Single().Conclusion);
        Assert.Empty(counted.Events.OfType<SeatStateChangedEvent>());

        // 裁决「不受保护」的同款流程走另一分支（开一局新的第一天做对照）。
        var dayTwo = ExilePhaseFixture.StartDay();
        var secondContext = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "barista", LifeState.Alive),
                (3, "barista", LifeState.Alive)),
            ctx => ctx.Day?.ProtectionDecisionFor(ctx.Seat) is { } decision
                ? decision.Protected
                    ? Protected("已裁定：受保护")
                    : NotProtected("已裁定：不受保护")
                : NeedsRuling("还没裁定"));
        var proposal = ExilePhaseFixture.Propose(dayTwo, secondContext, proposer: 1, target: 2);
        var sweptSecond = ExilePhaseFixture.RunSweep(proposal.State, secondContext, 1, 1, 2);
        var unprotected = Resolve(sweptSecond, secondContext, seat: 2, isProtected: false);
        Assert.Equal(StepMachineOutcomeKind.Applied, unprotected.Kind);
        var countedUnprotected = ExilePhaseFixture.Count(unprotected.State, secondContext, 1);
        Assert.Equal(
            ExileConclusion.Exiled,
            countedUnprotected.Events.OfType<ExileVoteCountedEvent>().Single().Conclusion);
        Assert.Single(countedUnprotected.Events.OfType<SeatStateChangedEvent>());
    }

    [Fact]
    public void ResolveProtection_WithoutARulingToMake_IsRejected()
    {
        // 来源说"这件事根本不归我管"（不受保护）：没有待裁定的东西，裁定被拒。
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "barista", LifeState.Alive),
                (3, "barista", LifeState.Alive)),
            _ => NotProtected("测试来源：不受保护"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var swept = ExilePhaseFixture.RunSweep(proposed.State, context, 1, 1, 2);
        var resolved = Resolve(swept, context, seat: 2, isProtected: true);

        Assert.Equal(StepMachineOutcomeKind.Rejected, resolved.Kind);
        Assert.Equal("day.protection_not_required", resolved.RejectionCode);
    }

    [Fact]
    public void CloseDay_ProtectionAppliesToTheExecutionCloseOut()
    {
        var context = DayPhaseFixture.Context(
            (1, LifeState.Alive),
            (2, LifeState.Alive),
            (3, LifeState.Alive));

        var nominated = DayPhaseFixture.Nominate(DayPhaseFixture.StartDay(), context, 1, 2);
        var swept = DayPhaseFixture.RunSweep(nominated.State, context, 1, 1, 2);
        var counted = DayPhaseFixture.Count(swept, context, 1);
        Assert.Equal(new SeatId(2), counted.State.Day!.OpenDay!.AboutToBeExecuted);

        // 受保护：只记「被处决」，不产生死亡（处决 ≠ 死亡，百科《处决》）。
        var protectedContext = context with
        {
            DeathProtections =
            [
                new ScriptedProtectionSource(ctx =>
                    ctx.Seat.Value == 2 && ctx.Cause == DeathProtectionCause.Execution
                        ? Protected("测试来源：处决保护")
                        : null),
            ],
        };
        var closed = DayPhaseFixture.Close(counted.State, protectedContext);
        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        Assert.Contains(closed.Events.OfType<ExecutedEvent>(), executed => executed.Seat.Value == 2);
        Assert.Empty(closed.Events.OfType<SeatStateChangedEvent>());

        // 待裁定 / 判定不了：显式拒绝，不猜、不静默死亡。
        var undecidedContext = context with
        {
            DeathProtections = [new ScriptedProtectionSource(_ => NeedsRuling("测试来源：处决待裁定"))],
        };
        var blocked = DayPhaseFixture.Close(counted.State, undecidedContext);
        Assert.Equal(StepMachineOutcomeKind.Rejected, blocked.Kind);
        Assert.Equal("day.execution_protection_required", blocked.RejectionCode);

        var indeterminateContext = context with
        {
            DeathProtections = [new ScriptedProtectionSource(_ => Indeterminate("测试来源：处决判定不了"))],
        };
        var unknown = DayPhaseFixture.Close(counted.State, indeterminateContext);
        Assert.Equal(StepMachineOutcomeKind.Rejected, unknown.Kind);
        Assert.Equal("day.execution_protection_indeterminate", unknown.RejectionCode);
    }

    /// <summary>
    /// 第三条致死路径：处罚处决（R-0020）与 <see cref="DayMachine.CloseDay"/> 同源先问保护（R-0048 第 6 条）。
    /// 白天形态：受保护只记「被处决」、不产生死亡，但处决事实照占当天上限并收口白天；待裁定 / 判定不了整条拒绝。
    /// </summary>
    [Fact]
    public void PunishmentExecution_ProtectionAppliesToTheAdjudicatedExecution()
    {
        var context = PunishmentContext();

        var protectedContext = WithProtection(
            context,
            ctx => ctx.Seat.Value == 2 && ctx.Cause == DeathProtectionCause.Execution
                ? Protected("测试来源：处决保护")
                : null);
        var protectedOutcome = Punish(DayPhaseFixture.StartDay(), protectedContext, seat: 2);
        Assert.Equal(StepMachineOutcomeKind.Applied, protectedOutcome.Kind);
        Assert.Contains(protectedOutcome.Events.OfType<ExecutedEvent>(), executed => executed.Seat.Value == 2);
        Assert.Empty(protectedOutcome.Events.OfType<SeatStateChangedEvent>());
        Assert.Contains(protectedOutcome.Events, item => item is DayClosedEvent { DayNumber: 1 });
        Assert.Contains(protectedOutcome.Events, item => item is PhaseCompletedEvent);
        Assert.Null(protectedOutcome.State.Day!.OpenDay);

        // 保护不改变「处决 ≠ 死亡」的记账：当天上限照样被占（R-0020 第 1 条形状不变）。
        var day = Assert.Single(protectedOutcome.State.Day.Days);
        Assert.Equal(new SeatId(2), day.Executed);

        var undecided = WithProtection(context, _ => NeedsRuling("测试来源：处决待裁定"));
        var blocked = Punish(DayPhaseFixture.StartDay(), undecided, seat: 2);
        Assert.Equal(StepMachineOutcomeKind.Rejected, blocked.Kind);
        Assert.Equal("punishment.execution_protection_required", blocked.RejectionCode);
        Assert.Empty(blocked.Events);

        var indeterminate = WithProtection(context, _ => Indeterminate("测试来源：处决判定不了"));
        var unknown = Punish(DayPhaseFixture.StartDay(), indeterminate, seat: 2);
        Assert.Equal(StepMachineOutcomeKind.Rejected, unknown.Kind);
        Assert.Equal("punishment.execution_protection_indeterminate", unknown.RejectionCode);
        Assert.Empty(unknown.Events);
    }

    /// <summary>夜晚处罚处决同样先问保护；保护不改变夜晚形态（不写白天账、不推进阶段，R-0020 第 2 条）。</summary>
    [Fact]
    public void NightPunishmentExecution_ProtectionApplies_WithoutReshapingTheNight()
    {
        var context = WithProtection(
            PunishmentContext(),
            ctx => ctx.Cause == DeathProtectionCause.Execution ? Protected("测试来源：处决保护") : null);
        var night = StepMachine.StartPhase(DayPhaseFixture.NightPlan()).State;

        var outcome = Punish(night, context, seat: 2);

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var executed = Assert.Single(outcome.Events.OfType<ExecutedEvent>());
        Assert.Null(executed.DayNumber);
        Assert.Empty(outcome.Events.OfType<SeatStateChangedEvent>());
        Assert.DoesNotContain(outcome.Events, item => item is SlotAdvancedEvent or PhaseCompletedEvent);
        Assert.Equal(0, outcome.State.SlotIndex);
        Assert.Null(outcome.State.Day);

        // 待裁定 / 判定不了在夜晚形态同样整条拒绝（保护查询与昼夜分支无关，两条收口同源）。
        var undecided = WithProtection(PunishmentContext(), _ => NeedsRuling("测试来源：处决待裁定"));
        var blocked = Punish(StepMachine.StartPhase(DayPhaseFixture.NightPlan()).State, undecided, seat: 2);
        Assert.Equal(StepMachineOutcomeKind.Rejected, blocked.Kind);
        Assert.Equal("punishment.execution_protection_required", blocked.RejectionCode);
        Assert.Empty(blocked.Events);

        var indeterminate = WithProtection(PunishmentContext(), _ => Indeterminate("测试来源：处决判定不了"));
        var unknown = Punish(StepMachine.StartPhase(DayPhaseFixture.NightPlan()).State, indeterminate, seat: 2);
        Assert.Equal(StepMachineOutcomeKind.Rejected, unknown.Kind);
        Assert.Equal("punishment.execution_protection_indeterminate", unknown.RejectionCode);
        Assert.Empty(unknown.Events);
    }

    /// <summary>
    /// 阴性对照：来源不表态（只覆盖流放，怪咖口径 R-0048 第 1 条）或明确「不受保护」，处罚处决都照常致死
    /// ——行为与既有实现一致。
    /// </summary>
    [Fact]
    public void PunishmentExecution_WithoutAnExecutionSource_DiesAsBefore()
    {
        var silent = WithProtection(
            PunishmentContext(),
            ctx => ctx.Cause == DeathProtectionCause.Exile ? Protected("测试来源：只管流放") : null);
        var outcome = Punish(DayPhaseFixture.StartDay(), silent, seat: 2);
        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var death = Assert.Single(outcome.Events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);

        // 明确「不受保护」经聚合落到同一分支（默认说明 / 来源自己的说明都不改结论）。
        var explicitNo = WithProtection(PunishmentContext(), _ => NotProtected("测试来源：明确不受保护"));
        var stillDies = Punish(DayPhaseFixture.StartDay(), explicitNo, seat: 2);
        Assert.Equal(StepMachineOutcomeKind.Applied, stillDies.Kind);
        Assert.Single(stillDies.Events.OfType<SeatStateChangedEvent>());
    }

    /// <summary>已死亡目标不查保护：保护改变不了结果，条件链先于查询短路（与 CloseDay 同序，R-0020 第 3 条）。</summary>
    [Fact]
    public void PunishmentExecution_OnADeadSeat_SkipsTheProtectionQuery()
    {
        // 来源对任何死因都要求裁定：真被查到的话，这条处罚会被拒绝。
        var context = WithProtection(
            PunishmentContext() with
            {
                State = DayPhaseFixture.StateOf(
                    (1, LifeState.Alive),
                    (2, LifeState.Dead),
                    (3, LifeState.Alive)),
            },
            _ => NeedsRuling("测试来源：待裁定"));

        var outcome = Punish(DayPhaseFixture.Close(DayPhaseFixture.StartDay(), context).State, context, seat: 2);

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Single(outcome.Events.OfType<ExecutedEvent>());
        Assert.Empty(outcome.Events.OfType<SeatStateChangedEvent>());
    }

    [Fact]
    public void ProtectionLedger_FoldsOnlyReachedExileDecisionsAndRejectsDuplicates()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "barista", LifeState.Alive));
        var day = ExilePhaseFixture.StartDay();

        // 没有「收完且达线」的流放：事件流里出现裁定 = 损坏，恢复必须失败。
        var orphan = new DayProtectionDecidedEvent
        {
            DayNumber = 1,
            Seat = new SeatId(2),
            Protected = true,
        };
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(day, orphan));

        var proposed = ExilePhaseFixture.Propose(day, context, proposer: 1, target: 2);
        var swept = ExilePhaseFixture.RunSweep(proposed.State, context, 1, 1, 2);
        var applied = StepMachine.Apply(swept, orphan)
            ?? throw new InvalidOperationException("裁定折叠后丢失步骤机状态");
        Assert.NotNull(applied.Day!.OpenDay!.ProtectionDecisionFor(new SeatId(2)));

        // 同一席位同一天第二条裁定 = 损坏。
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(applied, orphan with { Protected = false }));
    }

    /// <summary>提示只在「收票收完 + 达线」之后出现；收票中 / 未达线一律没有入口（R-0048 第 2 条）。</summary>
    [Fact]
    public void ProtectionPromptQuery_AppearsOnlyAfterReachedSweep()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "harlot", LifeState.Alive),
                (3, "clockmaker", LifeState.Alive)),
            _ => NeedsRuling("测试来源：待裁定"));

        // 没有白天 / 没有流放：没有提示。
        Assert.Null(DayProtectionPromptQuery.ForOpenExile(context, dayState: null));
        var day = ExilePhaseFixture.StartDay();
        Assert.Null(DayProtectionPromptQuery.ForOpenExile(context, day.Day));

        // 有流放但还没开始收票：没有提示。
        var proposed = ExilePhaseFixture.Propose(day, context, proposer: 1, target: 2);
        Assert.Null(DayProtectionPromptQuery.ForOpenExile(context, proposed.State.Day));

        // 收票中：没有提示。
        var started = ExilePhaseFixture.StartSweep(proposed.State, context, index: 1);
        Assert.Null(DayProtectionPromptQuery.ForOpenExile(context, started.State.Day));

        // 收完但未达线：没有提示。
        var insufficient = ExilePhaseFixture.RunSweep(proposed.State, context, index: 1, raised: [1]);
        Assert.Null(DayProtectionPromptQuery.ForOpenExile(context, insufficient.Day));

        // 收完且达线：提示出现，席位 = 流放目标。
        var reached = ExilePhaseFixture.RunSweep(proposed.State, context, index: 1, raised: [1, 2]);
        var prompt = DayProtectionPromptQuery.ForOpenExile(context, reached.Day);
        Assert.NotNull(prompt);
        Assert.Equal(new SeatId(2), prompt.Seat);
        Assert.Equal(DeathProtectionOutcome.NeedsRuling, prompt.Outcome);
        Assert.Contains("待裁定", prompt.Note, StringComparison.Ordinal);
    }

    /// <summary>维度观测不齐时给 Indeterminate 提示（先补观测），不是裁定入口。</summary>
    [Fact]
    public void ProtectionPromptQuery_Indeterminate_IsPromptedWithObservationNote()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "harlot", LifeState.Alive),
                (3, "clockmaker", LifeState.Alive)),
            _ => Indeterminate("测试来源：判定不了"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var reached = ExilePhaseFixture.RunSweep(proposed.State, context, index: 1, raised: [1, 2]);

        var prompt = DayProtectionPromptQuery.ForOpenExile(context, reached.Day);
        Assert.NotNull(prompt);
        Assert.Equal(new SeatId(2), prompt.Seat);
        Assert.Equal(DeathProtectionOutcome.Indeterminate, prompt.Outcome);
        Assert.Contains("先补观测", prompt.Note, StringComparison.Ordinal);
    }

    /// <summary>没有来源要求裁定时不出现入口（来源明确「不受保护」）。</summary>
    [Fact]
    public void ProtectionPromptQuery_WithoutARulingToMake_IsNull()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "harlot", LifeState.Alive),
                (3, "clockmaker", LifeState.Alive)),
            _ => NotProtected("测试来源：不受保护"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var reached = ExilePhaseFixture.RunSweep(proposed.State, context, index: 1, raised: [1, 2]);

        Assert.Null(DayProtectionPromptQuery.ForOpenExile(context, reached.Day));
    }

    /// <summary>裁定记录之后入口消失（每席位每天至多一条）。</summary>
    [Fact]
    public void ProtectionPromptQuery_AfterTheDecision_IsNull()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "harlot", LifeState.Alive),
                (3, "clockmaker", LifeState.Alive)),
            ctx => ctx.Day?.ProtectionDecisionFor(ctx.Seat) is { } decision
                ? decision.Protected
                    ? Protected("已裁定：受保护")
                    : NotProtected("已裁定：不受保护")
                : NeedsRuling("还没裁定"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var reached = ExilePhaseFixture.RunSweep(proposed.State, context, index: 1, raised: [1, 2]);
        Assert.NotNull(DayProtectionPromptQuery.ForOpenExile(context, reached.Day));

        var resolved = Resolve(reached, context, seat: 2, isProtected: true);
        Assert.Equal(StepMachineOutcomeKind.Applied, resolved.Kind);
        Assert.Null(DayProtectionPromptQuery.ForOpenExile(context, resolved.State.Day));
    }

    /// <summary>目标已死时不出现入口：裁定改变不了结果，条件链先于查询短路。</summary>
    [Fact]
    public void ProtectionPromptQuery_WhenTargetIsAlreadyDead_IsNull()
    {
        var context = WithProtection(
            ExilePhaseFixture.Context(
                (1, "clockmaker", LifeState.Alive),
                (2, "harlot", LifeState.Dead),
                (3, "clockmaker", LifeState.Alive)),
            _ => NeedsRuling("测试来源：待裁定"));

        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        var reached = ExilePhaseFixture.RunSweep(proposed.State, context, index: 1, raised: [1, 2]);

        Assert.Null(DayProtectionPromptQuery.ForOpenExile(context, reached.Day));
    }

    private static StepMachineOutcome Resolve(
        StepMachineState state,
        SettlementContext context,
        int seat,
        bool isProtected) =>
        DayPhaseFixture.Apply(state, context, new ResolveDayProtectionInput
        {
            Seat = new SeatId(seat),
            Protected = isProtected,
        });

    private static SettlementContext WithProtection(
        SettlementContext context,
        Func<DeathProtectionContext, DeathProtectionAssessment?> evaluate) =>
        context with { DeathProtections = [new ScriptedProtectionSource(evaluate)] };

    private static DeathProtectionAssessment Protected(string note) =>
        new() { Outcome = DeathProtectionOutcome.Protected, Note = note };

    private static DeathProtectionAssessment NotProtected(string note) =>
        new() { Outcome = DeathProtectionOutcome.NotProtected, Note = note };

    private static DeathProtectionAssessment NeedsRuling(string note) =>
        new() { Outcome = DeathProtectionOutcome.NeedsRuling, Note = note };

    private static DeathProtectionAssessment Indeterminate(string note) =>
        new() { Outcome = DeathProtectionOutcome.Indeterminate, Note = note };

    private sealed class ScriptedProtectionSource(
        Func<DeathProtectionContext, DeathProtectionAssessment?> evaluate) : IDeathProtectionSource
    {
        public CharacterId Character => new("test.protection");

        public DeathProtectionAssessment? Evaluate(DeathProtectionContext context) => evaluate(context);
    }

    /// <summary>处罚处决（R-0020）的结算上下文：依据契约用最小替身，其余与白天夹具一致。</summary>
    private static SettlementContext PunishmentContext() =>
        DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive))
        with
        { AdjudicatedExecutions = [new AlwaysApplicablePunishment()] };

    /// <summary>走一条处罚处决输入。</summary>
    private static StepMachineOutcome Punish(StepMachineState state, SettlementContext context, int seat) =>
        DayPhaseFixture.Apply(state, context, new PunishExecutionInput
        {
            Seat = new SeatId(seat),
            Source = MadnessPunishmentSource.Cerenovus,
            Note = "测试：处罚处决",
        });

    /// <summary>处罚依据契约替身：本文件判的是死亡保护查询的接线，不是角色规则（规则在 Rules.Tests）。</summary>
    private sealed class AlwaysApplicablePunishment : IAdjudicatedExecutionSource
    {
        public MadnessPunishmentSource Source => MadnessPunishmentSource.Cerenovus;

        public AdjudicatedExecutionEligibility Evaluate(
            GameState state,
            IReadOnlyList<SeatId> seats,
            SeatId seat) => new()
            {
                Applicable = true,
                Note = "测试：依据成立",
                DeathReason = "测试：依据成立",
                CausedBy = new SeatId(1),
            };
    }
}
