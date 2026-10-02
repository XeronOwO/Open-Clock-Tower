using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 洗脑师与畸形秀演员的规则用例（走引擎使用的公开目录检索，与生产同一批实现）：
/// 两维选择提示、疯狂要求的签发与到期、两条处罚依据契约。
/// </summary>
/// <remarks>
/// 来源：百科《洗脑师》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记；
/// 百科《畸形秀演员》· 2026-10-01 抓取 · 角色简介；
/// 口径见 <c>docs/standard/rulings.md</c> R-0020 / R-0021。
/// </remarks>
public sealed class CerenovusAbilitiesTests
{
    private static readonly CharacterId Cerenovus = new("cerenovus");
    private static readonly CharacterId Mutant = new("mutant");
    private static readonly AbilityId MadnessAbility = new("cerenovus.madness");
    private static readonly SeatId SourceSeat = new(1);
    private static readonly SeatId TargetSeat = new(3);

    private static INightAction Prompt => NightActions.Default.Find(Cerenovus)!;

    private static IAbilityResolution Resolution => NightActions.Resolutions.Find(Cerenovus)!;

    private static IEventTrigger Trigger =>
        RoleContracts.EventTriggers.Single(trigger => trigger.Ability == MadnessAbility);

    private static IAdjudicatedExecutionSource CerenovusPunishment =>
        RoleContracts.AdjudicatedExecutions.Single(source => source.Source == MadnessPunishmentSource.Cerenovus);

    private static IAdjudicatedExecutionSource MutantPunishment =>
        RoleContracts.AdjudicatedExecutions.Single(source => source.Source == MadnessPunishmentSource.Mutant);

    /// <summary>目录登记：两维提示、结算契约、到期触发器与两条处罚依据同时可用。</summary>
    [Fact]
    public void Catalog_CoversCerenovusPromptResolutionTriggerAndPunishments()
    {
        Assert.Equal(Cerenovus, Prompt.Character);
        Assert.Equal(Cerenovus, Resolution.Character);
        Assert.Equal(new AbilityId("cerenovus"), Resolution.Ability);
        Assert.Equal(MadnessAbility, Trigger.Ability);
        Assert.Equal(2, RoleContracts.AdjudicatedExecutions.Count);
        Assert.True(DayActions.IsCovered(Cerenovus));
        Assert.True(DayActions.IsCovered(Mutant));
    }

    /// <summary>提示是「玩家 × 善良角色」两维：目标含自己与已死亡玩家；角色只列镇民与外来者。</summary>
    [Fact]
    public void Prompt_OffersAllSeatsAndGoodCharactersOnly()
    {
        var state = GameStateMachine.Fold(
        [
            Seat(1, "cerenovus", LifeState.Alive),
            Seat(2, "clockmaker", LifeState.Alive),
            Seat(3, "dreamer", LifeState.Dead),
            Seat(4, "no-dashii", LifeState.Alive),
        ]);

        var prompt = Prompt.BuildPrompt(new NightActionContext
        {
            Actor = SourceSeat,
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3), new SeatId(4)],
            State = state,
        });

        Assert.True(prompt.HasSecondDimension);
        Assert.Equal(
            ["seat:1", "seat:2", "seat:3", "seat:4"],
            prompt.Options.Select(option => option.Value));

        var characters = prompt.SecondaryOptions.Select(option => option.Value).ToArray();
        Assert.Equal(17, characters.Length);
        Assert.Contains("clockmaker", characters);
        Assert.Contains("mutant", characters);
        Assert.DoesNotContain("witch", characters);
        Assert.DoesNotContain("no-dashii", characters);
    }

    /// <summary>生效：写入要求（身份 / 来源 / 到期日）并给目标一条私密告知。</summary>
    [Fact]
    public void Resolve_Effective_IssuesRequirementAndInformsTheTarget()
    {
        var events = Resolution.Resolve(Context("seat:3|savant", effective: true, daysStarted: 1));

        var issued = Assert.IsType<MadnessRequirementIssuedEvent>(events[0]);
        Assert.Equal(new MadnessRequirementId("sv:night-2:cerenovus:madness"), issued.Requirement.Id);
        Assert.Equal(TargetSeat, issued.Requirement.Seat);
        Assert.Equal("博学者", issued.Requirement.ProveToBe);
        Assert.Equal(SourceSeat, issued.Requirement.Source);
        Assert.Equal(Cerenovus, issued.Requirement.SourceCharacter);
        Assert.Equal(MadnessAbility, issued.Requirement.Ability);
        Assert.Equal(3, issued.Requirement.ExpiresAtDay);

        var information = Assert.IsType<InformationResultIssuedEvent>(events[1]);
        Assert.Equal(TargetSeat, information.Recipient);
        Assert.Equal(MadnessAbility, information.Ability);
        Assert.Contains("洗脑师", information.Content, StringComparison.Ordinal);
        Assert.Contains("博学者", information.Content, StringComparison.Ordinal);
    }

    /// <summary>行动当时醉酒 / 中毒：不写要求、不告知目标（百科《洗脑师》提示标记的放置条件）。</summary>
    [Fact]
    public void Resolve_Ineffective_ProducesNothing()
    {
        Assert.Empty(Resolution.Resolve(Context("seat:3|savant", effective: false, daysStarted: 0)));
    }

    /// <summary>答案必须是两维编码：少一维或选了邪恶角色一律显式拒绝。</summary>
    [Fact]
    public void Resolve_IllegalComposite_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(
            () => Resolution.Resolve(Context("seat:3", effective: true, daysStarted: 0)));
        Assert.Throws<InvalidOperationException>(
            () => Resolution.Resolve(Context("seat:3|witch", effective: true, daysStarted: 0)));
    }

    /// <summary>到期收口：存续窗口内的白天开始不撤下；到期日的白天开始撤下（R-0021）。</summary>
    [Fact]
    public void Trigger_ExpiresOnTheNextDawn_AndIsIdempotent()
    {
        var state = WithRequirement(expiresAtDay: 2);
        var seats = new[] { SourceSeat, TargetSeat };

        var before = Trigger.Evaluate(new EventTriggerContext
        {
            State = state,
            Seats = seats,
            Events = [new DayStartedEvent { DayNumber = 1 }],
        });
        Assert.Empty(before);

        var due = Trigger.Evaluate(new EventTriggerContext
        {
            State = state,
            Seats = seats,
            Events = [new DayStartedEvent { DayNumber = 2 }],
        });
        var terminated = Assert.IsType<MadnessRequirementTerminatedEvent>(Assert.Single(due));
        Assert.Equal(new MadnessRequirementId("sv:night-1:cerenovus:madness"), terminated.Id);
        Assert.Equal(EffectTerminationKind.NoLongerApplies, terminated.Termination.Kind);

        // 幂等：撤下后的账里不再有"未撤下"的要求，重复求值不产出第二条。
        var after = GameStateMachine.Apply(state, terminated);
        Assert.Empty(Trigger.Evaluate(new EventTriggerContext
        {
            State = after,
            Seats = seats,
            Events = [new DayStartedEvent { DayNumber = 2 }],
        }));
    }

    /// <summary>一批里有多条白天开始（重建 / 补批）时按最晚那天一次性到期，不静默漏撤（对抗性复核 F-5）。</summary>
    [Fact]
    public void Trigger_HandlesMultipleDayStartsInOneBatch()
    {
        var due = Trigger.Evaluate(new EventTriggerContext
        {
            State = WithRequirement(expiresAtDay: 2),
            Seats = [SourceSeat, TargetSeat],
            Events = [new DayStartedEvent { DayNumber = 1 }, new DayStartedEvent { DayNumber = 2 }],
        });

        var terminated = Assert.IsType<MadnessRequirementTerminatedEvent>(Assert.Single(due));
        Assert.Equal(EffectTerminationKind.NoLongerApplies, terminated.Termination.Kind);
    }

    /// <summary>洗脑师处罚依据：要求未撤下且来源生效才成立；来源不生效显式不成立；来源状态没观测齐不猜。</summary>
    [Fact]
    public void Punishment_Cerenovus_RequiresLiveOperativeRequirement()
    {
        var live = WithRequirement(expiresAtDay: 2);

        var applicable = CerenovusPunishment.Evaluate(live, [SourceSeat, TargetSeat], TargetSeat);
        Assert.True(applicable.Applicable);
        Assert.Equal(SourceSeat, applicable.CausedBy);
        Assert.Equal(new EffectId("sv:night-1:cerenovus:madness"), applicable.EffectId);
        Assert.Contains("钟表匠", Assert.IsType<string>(applicable.DeathReason), StringComparison.Ordinal);

        var missing = CerenovusPunishment.Evaluate(
            GameStateMachine.Fold([Seat(1, "cerenovus", LifeState.Alive)]),
            [SourceSeat, TargetSeat],
            TargetSeat);
        Assert.False(missing.Applicable);

        var sourceDead = CerenovusPunishment.Evaluate(
            GameStateMachine.Apply(
                live,
                new SeatStateChangedEvent { Seat = SourceSeat, Life = LifeState.Dead, Reason = "被处决" }),
            [SourceSeat, TargetSeat],
            TargetSeat);
        Assert.False(sourceDead.Applicable);

        var sourceDrunk = CerenovusPunishment.Evaluate(
            GameStateMachine.Apply(
                live,
                new SeatStateChangedEvent { Seat = SourceSeat, Drunk = DrunkState.Drunk, Reason = "涡流能力" }),
            [SourceSeat, TargetSeat],
            TargetSeat);
        Assert.False(sourceDrunk.Applicable);
    }

    /// <summary>畸形秀演员处罚依据：席位必须是畸形秀演员，且能力生效（存活 / 清醒 / 健康）。</summary>
    [Fact]
    public void Punishment_Mutant_RequiresTheLivingSoberMutant()
    {
        SeatId[] seats = [SourceSeat, TargetSeat];

        Assert.True(MutantPunishment.Evaluate(
            GameStateMachine.Fold([Seat(1, "mutant", LifeState.Alive)]), seats, SourceSeat).Applicable);

        Assert.False(MutantPunishment.Evaluate(
            GameStateMachine.Fold([Seat(1, "clockmaker", LifeState.Alive)]), seats, SourceSeat).Applicable);

        Assert.False(MutantPunishment.Evaluate(
            GameStateMachine.Fold([Seat(1, "mutant", LifeState.Dead)]), seats, SourceSeat).Applicable);

        Assert.False(MutantPunishment.Evaluate(
            GameStateMachine.Apply(
                GameStateMachine.Fold([Seat(1, "mutant", LifeState.Alive)]),
                new SeatStateChangedEvent { Seat = SourceSeat, Drunk = DrunkState.Drunk, Reason = "涡流能力" }),
            seats,
            SourceSeat).Applicable);

        // 角色还没观测 / 生死也没观测：判定不了，返回 null（不猜，D-0015）。
        Assert.Null(MutantPunishment.Evaluate(GameState.Empty, seats, SourceSeat).Applicable);
        Assert.Null(MutantPunishment.Evaluate(
            GameStateMachine.Fold([new SeatStateChangedEvent
            {
                Seat = SourceSeat,
                Character = Mutant,
                Reason = "测试：只观测到角色",
            }]),
            seats,
            SourceSeat).Applicable);
    }

    private static readonly MadnessRequirementId RequirementId = new("sv:night-1:cerenovus:madness");

    private static GameState WithRequirement(int expiresAtDay) =>
        GameStateMachine.Fold(
        [
            Seat(1, "cerenovus", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy),
            new MadnessRequirementIssuedEvent
            {
                Requirement = new MadnessRequirement
                {
                    Id = RequirementId,
                    Seat = TargetSeat,
                    ProveToBe = "钟表匠",
                    Source = SourceSeat,
                    SourceCharacter = Cerenovus,
                    Ability = MadnessAbility,
                    ExpiresAtDay = expiresAtDay,
                },
            },
        ]);

    private static SeatStateChangedEvent Seat(
        int seat,
        string character,
        LifeState life,
        DrunkState drunk = DrunkState.Sober,
        PoisonState poison = PoisonState.Healthy) => new()
        {
            Seat = new SeatId(seat),
            Character = new CharacterId(character),
            Life = life,
            Drunk = drunk,
            Poison = poison,
            Reason = "test.setup",
        };

    private static AbilityResolutionContext Context(string choice, bool effective, int daysStarted) => new()
    {
        SlotId = new StepSlotId("cerenovus"),
        PlanLabel = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Actor = SourceSeat,
        ActorCharacter = Cerenovus,
        Seats = [new SeatId(1), new SeatId(2), new SeatId(3), new SeatId(4)],
        State = GameStateMachine.Fold([Seat(1, "cerenovus", LifeState.Alive)]),
        Outcome = new AbilityOutcome
        {
            Effective = effective,
            Malfunction = effective ? null : MalfunctionKind.Poisoned,
        },
        Choice = choice,
        DaysStarted = daysStarted,
    };
}
