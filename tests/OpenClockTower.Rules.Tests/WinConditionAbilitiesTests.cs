using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 胜负相关角色的契约回归：镜像双子首夜配对与互认、涡流夜间击杀与「信息必假」标注（R-0025 / R-0026 / R-0028）。
/// </summary>
/// <remarks>
/// 通过公开目录 <see cref="NightActions"/> 取契约——与运行时建表 / 结算取的是同一批对象。
/// </remarks>
public sealed class WinConditionAbilitiesTests
{
    private static readonly CharacterId EvilTwin = new("evil-twin");
    private static readonly CharacterId Vortox = new("vortox");

    /// <summary>镜像双子配对：候选只含对立阵营；结算产出配对效果 + 双向互认两条信息（百科《镜像双子》）。</summary>
    [Fact]
    public void EvilTwinPairing_OffersOppositeAlignmentOnly_AndAppliesPairingWithMutualReveal()
    {
        var state = State(
            (1, "evil-twin", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive),
            (3, "no-dashii", Alignment.Evil, LifeState.Alive));
        var action = NightActions.Default.Find(EvilTwin);
        var resolution = NightActions.Resolutions.Find(EvilTwin);
        Assert.NotNull(action);
        Assert.NotNull(resolution);

        var prompt = action!.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(1),
            Seats = Seats(3),
            State = state,
        });
        Assert.Equal(["seat:2"], prompt.Options.Select(option => option.Value));

        var events = resolution!.Resolve(Resolution(state, actor: 1, choice: "seat:2"));

        var applied = Assert.IsType<PersistentEffectAppliedEvent>(events[0]);
        Assert.Equal("evil-twin.pair", applied.Effect.Ability.Value);
        Assert.Equal(new SeatId(1), applied.Effect.Source);
        Assert.Equal(new SeatId(2), applied.Effect.Target);
        Assert.Null(applied.Effect.Dimension);

        var information = events.OfType<InformationResultIssuedEvent>().ToArray();
        Assert.Equal(2, information.Length);
        Assert.Contains(
            information,
            item => item.Recipient == new SeatId(1) && item.Content.Contains("钟表匠", StringComparison.Ordinal));
        Assert.Contains(
            information,
            item => item.Recipient == new SeatId(2) && item.Content.Contains("镜像双子", StringComparison.Ordinal));
    }

    /// <summary>配对来源能力不生效（醉酒 / 中毒 / 死亡）→ 不放置配对标记、也不互认。</summary>
    [Fact]
    public void EvilTwinPairing_WhenIneffective_DoesNothing()
    {
        var state = State(
            (1, "evil-twin", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive));

        var events = NightActions.Resolutions.Find(EvilTwin)!
            .Resolve(Resolution(state, actor: 1, choice: "seat:2", effective: false));

        Assert.Empty(events);
    }

    /// <summary>涡流夜间击杀：目标死亡（可归因）；能力不生效时不产出任何死亡事实。</summary>
    [Fact]
    public void VortoxKill_TargetDies_AndIneffectiveProducesNothing()
    {
        var state = State(
            (1, "vortox", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive));
        var resolution = NightActions.Resolutions.Find(Vortox);
        Assert.NotNull(resolution);

        var events = resolution!.Resolve(Resolution(
            state,
            actor: 1,
            choice: "seat:2",
            phase: GamePhase.OtherNight,
            slot: "vortox"));

        Assert.Contains(events, item => item is SeatStateChangedEvent
        {
            Seat: { Value: 2 },
            Life: LifeState.Dead,
        });
        Assert.Empty(resolution.Resolve(Resolution(
            state,
            actor: 1,
            choice: "seat:2",
            phase: GamePhase.OtherNight,
            slot: "vortox",
            effective: false)));
    }

    /// <summary>涡流在场：钟表匠的信息被标成"可能为假"并写明必须为假（R-0028）。</summary>
    [Fact]
    public void VortoxInPlay_FlagsInformationAndExplainsWhy()
    {
        var state = State(
            (1, "vortox", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive),
            (3, "no-dashii", Alignment.Evil, LifeState.Alive));

        var events = NightActions.Resolutions.Find(new CharacterId("clockmaker"))!
            .Resolve(Resolution(state, actor: 2, decision: "3", slot: "clockmaker"));

        var information = Assert.IsType<InformationResultIssuedEvent>(Assert.Single(events));
        Assert.True(information.MayBeFalse);
        Assert.Contains("涡流", information.Note!, StringComparison.Ordinal);
    }

    /// <summary>涡流在场：筑梦师的信息不能继续按"一真一假"拼（那会拼出一条真信息）→ 退回自由填写。</summary>
    [Fact]
    public void VortoxInPlay_DreamerPostChoiceBecomesFreeText()
    {
        var state = State(
            (1, "vortox", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Alive));
        var resolution = NightActions.Resolutions.Find(new CharacterId("dreamer"));
        Assert.NotNull(resolution);

        var prompt = resolution!.BuildPostChoiceDecision(Resolution(
            state,
            actor: 3,
            choice: "seat:2",
            slot: "dreamer"));

        Assert.NotNull(prompt);
        Assert.Empty(prompt!.Options);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
        Assert.Contains("涡流", prompt.Context, StringComparison.Ordinal);
    }

    private static AbilityResolutionContext Resolution(
        GameState state,
        int actor,
        string? choice = null,
        string? decision = null,
        bool effective = true,
        GamePhase phase = GamePhase.FirstNight,
        string slot = "test-slot") =>
        new()
        {
            SlotId = new StepSlotId(slot),
            PlanLabel = "sv:night-1",
            Phase = phase,
            Actor = new SeatId(actor),
            ActorCharacter = state.Seat(new SeatId(actor))!.CharacterValue!.Value,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome { Effective = effective },
            Choice = choice,
            Decision = decision,
            DaysStarted = 0,
        };

    private static IReadOnlyList<SeatId> Seats(int count) =>
        [.. Enumerable.Range(1, count).Select(value => new SeatId(value))];

    private static GameState State(
        params (int Seat, string Character, Alignment Alignment, LifeState Life)[] rows) =>
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
