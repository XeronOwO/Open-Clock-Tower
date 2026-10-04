using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 流莺的规则回归（平台口径见 <c>docs/standard/rulings.md</c> R-0051）：
/// 其他夜晚 · 黄昏选一名存活玩家；同意与「两人今晚同死」由说书人一条裁定收口；
/// 能力未生效时展示照常、内容可能为假、且不产生同死。
/// </summary>
/// <remarks>来源：百科《流莺》· 2026-10-04 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记。</remarks>
public sealed class HarlotNightActionTests
{
    private static readonly CharacterId Harlot = new("harlot");
    private static readonly SeatId Actor = new(1);

    /// <summary>目标集合：只有存活席位进候选（含流莺自己——来源没有排除条款），生死未观测的席位不进。</summary>
    [Fact]
    public void Prompt_OffersAliveSeatsOnly_IncludingSelf()
    {
        var state = NightLedger(
            (1, "harlot", LifeState.Alive),
            (2, "clockmaker", LifeState.Alive),
            (3, "dreamer", LifeState.Dead),
            (4, "witch", LifeState.Alive),
            (5, "vortox", LifeState.Alive));
        var prompt = Prompt(state);

        var values = prompt.Options.Select(option => option.Value).ToArray();
        Assert.Equal(["seat:1", "seat:2", "seat:4", "seat:5"], values);
        Assert.Equal(NoOptionBehavior.BlockAndAlert, prompt.OnNoOption);
    }

    /// <summary>生死未观测的席位同样不进候选（不猜，D-0015）。</summary>
    [Fact]
    public void Prompt_SkipsSeatsWithUnobservedLife()
    {
        var ledger = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));
        var state = ledger with
        {
            Seats = [.. ledger.Seats.Select(entry => entry.Seat.Value == 3 ? entry with { Life = null } : entry)],
        };

        Assert.DoesNotContain(Prompt(state).Options, option => option.Value == "seat:3");
    }

    /// <summary>能力生效：一条裁定收口「拒绝 / 同意 / 同意且同死」，预览带上按账推演的真实角色。</summary>
    [Fact]
    public void Decision_Effective_OffersThreeOutcomes_WithTrueCharacterPreview()
    {
        var state = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));

        var prompt = Decision(state, "seat:2");

        Assert.Equal(["refuse", "agree", "agree-kill"], prompt.Options.Select(option => option.Value));
        Assert.Contains("钟表匠", prompt.Options[1].Preview, StringComparison.Ordinal);
        Assert.Contains("同死", prompt.Options[2].Preview, StringComparison.Ordinal);
        Assert.Contains("2 号玩家", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>
    /// 能力未生效（醉酒 / 中毒）：展示照常、由说书人挑要展示的角色标记（内容可能为假），
    /// 且不提供同死选项。
    /// </summary>
    [Fact]
    public void Decision_Ineffective_OffersTokenChoices_WithoutKill()
    {
        var state = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));

        var prompt = Decision(state, "seat:2", effective: false);

        var values = prompt.Options.Select(option => option.Value).ToArray();
        Assert.Equal("refuse", values[0]);
        Assert.Equal(SectsAndVioletsRoster.All.Count + 1, values.Length);
        Assert.Contains("agree:clockmaker", values);
        Assert.Contains("agree:witch", values);
        Assert.DoesNotContain("agree-kill", values);
        Assert.Contains("未生效", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>拒绝：无事发生——不产信息、不产死亡。</summary>
    [Fact]
    public void Resolve_Refuse_EmitsNothing()
    {
        var state = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));

        Assert.Empty(Contract().Resolve(Context(state, "seat:2", decision: "refuse")));
    }

    /// <summary>同意：信息只发给流莺本人，只报角色、不报阵营。</summary>
    [Fact]
    public void Resolve_Agree_IssuesTrueCharacterToSelf()
    {
        var state = NightLedger(
            (1, "harlot", LifeState.Alive, Alignment.Good),
            (2, "clockmaker", LifeState.Alive, Alignment.Good),
            (3, "dreamer", LifeState.Alive, Alignment.Evil));

        var information = Assert.Single(
            Contract().Resolve(Context(state, "seat:3", decision: "agree"))
                .OfType<InformationResultIssuedEvent>());

        Assert.Equal(Actor, information.Recipient);
        Assert.Equal(new AbilityId("harlot"), information.Ability);
        Assert.Contains("3 号玩家的角色是「筑梦师」", information.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("阵营", information.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("邪恶", information.Content, StringComparison.Ordinal);
        Assert.False(information.MayBeFalse);
    }

    /// <summary>
    /// 同意且同死：流莺与被选中玩家各落一条死亡事实（归因 = 流莺、即时型效果 + 状态变化），
    /// 信息照发；夜死累积到黎明公告（R-0022 / R-0045）。
    /// </summary>
    [Fact]
    public void Resolve_AgreeAndDie_KillsBothWithAttribution()
    {
        var state = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));

        var events = Contract().Resolve(Context(state, "seat:2", decision: "agree-kill"));

        Assert.Single(events.OfType<InformationResultIssuedEvent>());
        var deaths = events.OfType<SeatStateChangedEvent>().ToArray();
        Assert.Equal(2, deaths.Length);
        Assert.Equal([Actor, new SeatId(2)], deaths.Select(death => death.Seat));
        Assert.All(deaths, death =>
        {
            Assert.Equal(LifeState.Dead, death.Life);
            Assert.Equal(Actor, death.CausedBy);
            Assert.NotNull(death.EffectId);
            Assert.Contains("同死", death.Reason, StringComparison.Ordinal);
        });
        Assert.Equal(2, events.OfType<InstantaneousEffectAppliedEvent>().Count());
    }

    /// <summary>目标就是流莺自己（来源没有排除条款）：同死裁定只落一条死亡事实，不重复记账。</summary>
    [Fact]
    public void Resolve_AgreeAndDie_WhenTargetIsSelf_KillsOnce()
    {
        var state = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));

        var events = Contract().Resolve(Context(state, "seat:1", decision: "agree-kill"));

        Assert.Single(events.OfType<InformationResultIssuedEvent>());
        var death = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(Actor, death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
        Assert.Contains("同死", death.Reason, StringComparison.Ordinal);
    }

    /// <summary>能力未生效：展示的角色标记由说书人给出、信息标「可能为假」，且没有死亡事件。</summary>
    [Fact]
    public void Resolve_Ineffective_IssuesFalseMarkedContent_WithoutDeaths()
    {
        var state = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));

        var events = Contract().Resolve(Context(
            state,
            "seat:2",
            effective: false,
            decision: "agree:vortox"));

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Contains("涡流", information.Content, StringComparison.Ordinal);
        Assert.True(information.MayBeFalse);
        Assert.NotNull(information.Note);
        Assert.Empty(events.OfType<SeatStateChangedEvent>());
    }

    /// <summary>裁定值越界（不是三个取值之一、也不是花名册里的角色标记）：显式抛错，不当成拒绝。</summary>
    [Theory]
    [InlineData("maybe")]
    [InlineData("agree:not-a-character")]
    [InlineData("")]
    public void Resolve_InvalidDecision_Throws(string decision)
    {
        var state = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));

        Assert.Throws<InvalidOperationException>(() =>
            Contract().Resolve(Context(state, "seat:2", decision: decision)));
    }

    /// <summary>目标已死亡：来源要求「一名存活的玩家」，契约显式失败（不静默当无事发生）。</summary>
    [Fact]
    public void Resolve_DeadTarget_Throws()
    {
        var state = NightLedger(
            (1, "harlot", LifeState.Alive),
            (2, "clockmaker", LifeState.Dead),
            (3, "dreamer", LifeState.Alive));

        Assert.Throws<InvalidOperationException>(() =>
            Contract().Resolve(Context(state, "seat:2", decision: "agree")));
    }

    /// <summary>目标的角色未观测：列不出要展示的内容，显式失败（不猜，D-0015）。</summary>
    [Fact]
    public void Decision_UnobservedTargetCharacter_Throws()
    {
        var ledger = NightLedger((1, "harlot"), (2, "clockmaker"), (3, "dreamer"));
        var state = ledger with
        {
            Seats = [.. ledger.Seats.Select(entry => entry.Seat.Value == 2 ? entry with { Character = null } : entry)],
        };

        Assert.Throws<InvalidOperationException>(() => Decision(state, "seat:2"));
    }

    /// <summary>首个夜晚不行动是顺序表的口径（表上只在其他夜晚出现流莺）。</summary>
    [Fact]
    public void FirstNight_HasNoHarlotSlot()
    {
        foreach (var variant in new[] { NightOrderVariant.Original, NightOrderVariant.Recommended })
        {
            Assert.DoesNotContain(
                NightOrderTable.For(GamePhase.FirstNight, variant),
                entry => entry.Character == Harlot);
            Assert.Contains(
                NightOrderTable.For(GamePhase.OtherNight, variant),
                entry => entry.Character == Harlot);
        }
    }

    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(Harlot)
        ?? throw new InvalidOperationException("流莺没有注册结算契约");

    private static ChoicePrompt Prompt(GameState state) =>
        (NightActions.Default.Find(Harlot)
            ?? throw new InvalidOperationException("流莺没有注册提示契约")).BuildPrompt(new NightActionContext
            {
                Actor = Actor,
                Seats = [.. state.Seats.Select(entry => entry.Seat)],
                State = state,
            });

    private static ChoicePrompt Decision(GameState state, string choice, bool effective = true) =>
        Contract().BuildPostChoiceDecision(Context(state, choice, effective))
        ?? throw new InvalidOperationException("流莺的夜访必须开出说书人裁定点");

    private static AbilityResolutionContext Context(
        GameState state,
        string? choice,
        bool effective = true,
        string? decision = null) => new()
        {
            SlotId = new StepSlotId("harlot"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = Actor,
            ActorCharacter = Harlot,
            ActorOwnCharacter = Harlot,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
                Note = effective ? null : "来源中毒：能力未生效",
            },
            Choice = choice,
            Decision = decision,
            DaysStarted = 1,
        };

    private static GameState NightLedger(
        params (int Seat, string Character)[] rows) =>
        NightLedger([.. rows.Select(row => (row.Seat, row.Character, LifeState.Alive, Alignment.Good))]);

    private static GameState NightLedger(
        params (int Seat, string Character, LifeState Life)[] rows) =>
        NightLedger([.. rows.Select(row => (row.Seat, row.Character, row.Life, Alignment.Good))]);

    private static GameState NightLedger(
        params (int Seat, string Character, LifeState Life, Alignment Alignment)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(row.Alignment),
                    Life = Fact(row.Life),
                    Drunk = Fact(DrunkState.Sober),
                    Poison = Fact(PoisonState.Healthy),
                }),
            ],
        };

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = "测试夹具",
        };
}
