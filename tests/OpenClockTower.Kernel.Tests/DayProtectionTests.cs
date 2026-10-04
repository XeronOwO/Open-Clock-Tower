using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 统一死亡保护收口（票据 `traveller-and-exile` · D3 / R-0048）：
/// 流放计票与 <see cref="DayMachine.CloseDay"/> 的处决收口都先问保护；待裁定 / 判定不了显式拒绝。
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
}
