using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 旅行者加入 / 离场的账口径（`rulings.md` R-0044 第 6 条、R-0045）：加入是**事实**，
/// 六维度由配套的 <see cref="SeatStateChangedEvent"/> 落地；离场移除席位账、登记离场账，
/// 并终止以该席位为来源 / 目标的持续型效果与它下达的疯狂要求。
/// </summary>
/// <remarks>
/// 来源：百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式（加入第 2–3 步、离开流程）与
/// 票据 `docs/backlog/done/traveller-and-exile.md` D1 设计定稿。
/// </remarks>
public sealed class TravellerSeatLedgerTests
{
    private static readonly SeatId TravellerSeat = new(3);
    private static readonly SeatId OtherSeat = new(5);

    /// <summary>加入事件本身不改账；账由同批的 SeatStateChangedEvent 落地（事实与账分离）。</summary>
    [Fact]
    public void JoinedEvent_IsFactOnly_ThePairedSeatStateEventFoldsTheLedger()
    {
        var factOnly = GameStateMachine.Fold(
        [
            new TravellerJoinedEvent
            {
                Seat = TravellerSeat,
                Character = new CharacterId("deviant"),
                Alignment = Alignment.Evil,
            },
        ]);

        Assert.Null(factOnly.Seat(TravellerSeat));
        Assert.Empty(factOnly.DepartedSeats);

        var joined = GameStateMachine.Fold(
        [
            Seat(TravellerSeat, "deviant", Alignment.Evil),
            new TravellerJoinedEvent
            {
                Seat = TravellerSeat,
                Character = new CharacterId("deviant"),
                Alignment = Alignment.Evil,
            },
        ]);

        var entry = Assert.IsType<SeatStateEntry>(joined.Seat(TravellerSeat));
        Assert.Equal(new CharacterId("deviant"), entry.CharacterValue);
        Assert.Equal(Alignment.Evil, entry.Alignment?.Value);
        Assert.Equal(LifeState.Alive, entry.LifeValue);
        Assert.Empty(joined.DepartedSeats);
    }

    /// <summary>离场：席位账移除、离场账登记；相关持续型效果与疯狂要求以 SeatLeftGame 终止。</summary>
    [Fact]
    public void Departed_RemovesSeat_TerminatesRelatedEffectsAndRequirements()
    {
        var state = GameStateMachine.Fold(
        [
            Seat(OtherSeat, "clockmaker", Alignment.Good),
            Seat(TravellerSeat, "deviant", Alignment.Evil),
            new PersistentEffectAppliedEvent
            {
                Effect = Effect("test:effect-from-traveller", source: TravellerSeat, target: OtherSeat),
            },
            new PersistentEffectAppliedEvent
            {
                Effect = Effect("test:effect-on-traveller", source: OtherSeat, target: TravellerSeat),
            },
            new MadnessRequirementIssuedEvent { Requirement = Requirement("test:madness-from-traveller") },
        ]);

        var departed = GameStateMachine.Apply(
            state,
            new TravellerDepartedEvent { Seat = TravellerSeat, Note = "玩家需要提前离开（测试）" });

        Assert.Null(departed.Seat(TravellerSeat));
        Assert.NotNull(departed.Seat(OtherSeat));
        Assert.Equal([TravellerSeat], departed.DepartedSeats);
        Assert.True(departed.HasDeparted(TravellerSeat));

        var sourced = Assert.Single(departed.PersistentEffects, effect => effect.Id == new EffectId("test:effect-from-traveller"));
        Assert.Equal(EffectTerminationKind.SeatLeftGame, sourced.Termination?.Kind);
        Assert.Contains("离场", sourced.Termination?.Reason ?? string.Empty, StringComparison.Ordinal);

        var targeted = Assert.Single(departed.PersistentEffects, effect => effect.Id == new EffectId("test:effect-on-traveller"));
        Assert.Equal(EffectTerminationKind.SeatLeftGame, targeted.Termination?.Kind);

        // 要求记在**目标席位**的账上：来源离场要逐个目标扫，不能只删自己那一行。
        var requirement = Assert.Single(departed.Seat(OtherSeat)!.Madnesses);
        Assert.Equal(EffectTerminationKind.SeatLeftGame, requirement.Termination?.Kind);
    }

    /// <summary>事件流损坏：同一席位不能离场两次（不静默忽略第二次）。</summary>
    [Fact]
    public void DepartedTwice_Throws()
    {
        var state = GameStateMachine.Fold([Seat(TravellerSeat, "deviant", Alignment.Evil)]);
        var departed = GameStateMachine.Apply(state, new TravellerDepartedEvent { Seat = TravellerSeat });

        Assert.Throws<InvalidOperationException>(
            () => GameStateMachine.Apply(departed, new TravellerDepartedEvent { Seat = TravellerSeat }));
    }

    /// <summary>加入 / 离场都可以先于任何阶段出现：不允许把"尚未开始"（null）变成"已开始"。</summary>
    [Fact]
    public void TravellerEvents_DoNotStartTheStepMachine()
    {
        Assert.Null(StepMachine.Apply(
            null,
            new TravellerJoinedEvent
            {
                Seat = TravellerSeat,
                Character = new CharacterId("deviant"),
                Alignment = Alignment.Good,
            }));

        Assert.Null(StepMachine.Apply(null, new TravellerDepartedEvent { Seat = TravellerSeat }));
    }

    private static SeatStateChangedEvent Seat(SeatId seat, string character, Alignment alignment) =>
        new()
        {
            Seat = seat,
            Character = new CharacterId(character),
            Alignment = alignment,
            Life = LifeState.Alive,
            Drunk = DrunkState.Sober,
            Poison = PoisonState.Healthy,
            Reason = "测试夹具：初始观测",
        };

    private static PersistentEffect Effect(string id, SeatId source, SeatId target) =>
        new()
        {
            Id = new EffectId(id),
            Source = source,
            Ability = new AbilityId("test"),
            Target = target,
            SourceCharacter = new CharacterId("deviant"),
            Dimension = null,
        };

    private static MadnessRequirement Requirement(string id) =>
        new()
        {
            Id = new MadnessRequirementId(id),
            Seat = OtherSeat,
            ProveToBe = "怪咖",
            Source = TravellerSeat,
            SourceCharacter = new CharacterId("deviant"),
            Ability = new AbilityId("test"),
        };
}
