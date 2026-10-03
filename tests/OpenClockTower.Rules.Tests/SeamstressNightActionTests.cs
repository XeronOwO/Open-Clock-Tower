using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 女裁缝的规则回归（平台口径见 <c>docs/standard/rulings.md</c> R-0040）：
/// 每夜可选两名其他玩家（含已死）得知是否同阵营；摇头不用不消耗；用后不再唤醒。
/// </summary>
/// <remarks>来源：百科《女裁缝》· 2026-10-01 抓取 · 角色简介 1–3 / 运作方式 1–6。</remarks>
public sealed class SeamstressNightActionTests
{
    private static readonly CharacterId Seamstress = new("seamstress");
    private static readonly SeatId Actor = new(1);

    /// <summary>提示：除自己外的所有玩家对（5 席 = 6 对）+ 摇头；不含自己参与的 pair。</summary>
    [Fact]
    public void Prompt_OffersPlayerPairsAndDecline()
    {
        var prompt = Prompt();

        var values = prompt.Options.Select(option => option.Value).ToArray();
        Assert.Equal(7, values.Length);
        Assert.Contains("pair:2+3", values);
        Assert.Contains("pair:4+5", values);
        Assert.DoesNotContain("pair:1+2", values);
        Assert.DoesNotContain("pair:1+5", values);
        Assert.Equal("decline", values[^1]);
    }

    /// <summary>
    /// 候选只按席位集合生成、与生死无关：同样的席位构成下，有人死亡不改变候选
    /// （「无论他是生是死」，百科《女裁缝》· 角色简介 3）。
    /// </summary>
    [Fact]
    public void Prompt_OffersPairsRegardlessOfLife()
    {
        var alive = Prompt(NightLedger((1, "seamstress"), (2, "clockmaker"), (3, "dreamer")));
        var dead = Prompt(NightLedger(
            (1, "seamstress", LifeState.Alive),
            (2, "clockmaker", LifeState.Dead),
            (3, "dreamer", LifeState.Alive)));

        Assert.Equal(
            alive.Options.Select(option => option.Value),
            dead.Options.Select(option => option.Value));
        Assert.Contains("pair:2+3", dead.Options.Select(option => option.Value));
    }

    /// <summary>摇头：不进入裁定、不产出任何事件（无事发生）。</summary>
    [Fact]
    public void Decline_SkipsDecisionAndResolve()
    {
        var state = NightLedger((1, "seamstress"), (2, "clockmaker"), (3, "dreamer"));
        var contract = Contract();

        Assert.Null(contract.BuildPostChoiceDecision(Context(state, "decline")));
        Assert.Empty(contract.Resolve(Context(state, "decline")));
        Assert.False(contract.CountsAsUse(Context(state, "decline")));
    }

    /// <summary>选了对子：进入说书人裁定（是 / 否），上下文含按当前账的推演；使用会计入账本。</summary>
    [Fact]
    public void Pair_ListsYesNoDecision_WithLeanPreview()
    {
        var state = NightLedger(
            (1, "seamstress", LifeState.Alive, Alignment.Good),
            (2, "clockmaker", LifeState.Alive, Alignment.Good),
            (3, "dreamer", LifeState.Alive, Alignment.Evil));
        var contract = Contract();

        var prompt = contract.BuildPostChoiceDecision(Context(state, "pair:2+3"));
        Assert.NotNull(prompt);
        Assert.Equal(
            ["yes", "no"],
            prompt!.Options.Select(option => option.Value));
        Assert.Contains("不属于同一阵营", prompt.Context, StringComparison.Ordinal);
        Assert.True(contract.CountsAsUse(Context(state, "pair:2+3")));
    }

    /// <summary>「是」：信息只发给女裁缝本人，内容带上她选的两名玩家。</summary>
    [Fact]
    public void Resolve_Yes_IssuesInformationToSelf()
    {
        var state = NightLedger((1, "seamstress"), (2, "clockmaker"), (3, "dreamer"));

        var information = Assert.Single(
            Contract().Resolve(Context(state, "pair:2+3", decision: "yes"))
                .OfType<InformationResultIssuedEvent>());

        Assert.Equal(Actor, information.Recipient);
        Assert.Equal(new AbilityId("seamstress"), information.Ability);
        Assert.Contains("2 号与 3 号", information.Content, StringComparison.Ordinal);
        Assert.Contains("属于同一阵营", information.Content, StringComparison.Ordinal);
        Assert.False(information.MayBeFalse);
    }

    /// <summary>能力未生效（中毒 / 醉酒 / 死亡）：信息照发、标「可能为假」（百科《重要细节》三-1）。</summary>
    [Fact]
    public void Resolve_Ineffective_MarksMayBeFalse()
    {
        var state = NightLedger((1, "seamstress"), (2, "clockmaker"), (3, "dreamer"));

        var information = Assert.Single(
            Contract().Resolve(Context(state, "pair:2+3", effective: false, decision: "no"))
                .OfType<InformationResultIssuedEvent>());

        Assert.True(information.MayBeFalse);
        Assert.NotNull(information.Note);
    }

    /// <summary>涡流在场：信息必须为假（R-0028），平台只提示、不替说书人裁内容。</summary>
    [Fact]
    public void Resolve_WithVortox_MarksMustBeFalse()
    {
        var state = NightLedger(
            (1, "seamstress"),
            (2, "clockmaker"),
            (3, "dreamer"),
            (4, "vortox"));

        var context = Context(state, "pair:2+3", decision: "yes");
        var information = Assert.Single(Contract().Resolve(context).OfType<InformationResultIssuedEvent>());

        Assert.True(information.MayBeFalse);
        Assert.Contains("必须为假", information.Note!, StringComparison.Ordinal);
        Assert.Contains(
            MalfunctionKind.Vortox,
            Contract().InterferenceMalfunctions(context));
    }

    /// <summary>裁定值不是 是 / 否：事件流损坏，显式抛错（不静默当成摇头）。</summary>
    [Fact]
    public void Resolve_InvalidDecision_Throws()
    {
        var state = NightLedger((1, "seamstress"), (2, "clockmaker"), (3, "dreamer"));

        Assert.Throws<InvalidOperationException>(() =>
            Contract().Resolve(Context(state, "pair:2+3", decision: "maybe")));
    }

    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(Seamstress)
        ?? throw new InvalidOperationException("女裁缝没有注册结算契约");

    private static ChoicePrompt Prompt(GameState? state = null) =>
        (NightActions.Default.Find(Seamstress)
            ?? throw new InvalidOperationException("女裁缝没有注册提示契约")).BuildPrompt(new NightActionContext
            {
                Actor = Actor,
                Seats = state?.Seats.Select(entry => entry.Seat).ToArray()
                    ?? [new SeatId(1), new SeatId(2), new SeatId(3), new SeatId(4), new SeatId(5)],
                State = state ?? GameState.Empty,
            });

    private static AbilityResolutionContext Context(
        GameState state,
        string? choice,
        bool effective = true,
        string? decision = null) => new()
        {
            SlotId = new StepSlotId("seamstress"),
            PlanLabel = "sv:night-1",
            Phase = GamePhase.FirstNight,
            Actor = Actor,
            ActorCharacter = Seamstress,
            ActorOwnCharacter = Seamstress,
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
            DaysStarted = 0,
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
