using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 回溯型信息族（卖花女孩 / 城镇公告员 / 神谕者，R-0037）：推演读数按「动作时刻的角色快照」
/// 与「举手不撤销」判定，信息由说书人给出，涡流在场必须为假（R-0028），读不出来时不猜。
/// </summary>
public sealed class RetrospectiveInfoTests
{
    private static readonly SeatId FlowergirlSeat = new(1);
    private static readonly SeatId TownCrierSeat = new(2);
    private static readonly SeatId OracleSeat = new(3);
    private static readonly CharacterId Flowergirl = new("flowergirl");
    private static readonly CharacterId TownCrier = new("town-crier");
    private static readonly CharacterId Oracle = new("oracle");
    private static readonly CharacterId NoDashii = new("no-dashii");
    private static readonly CharacterId Witch = new("witch");
    private static readonly CharacterId Clockmaker = new("clockmaker");
    private static readonly CharacterId Vortox = new("vortox");

    /// <summary>建表与结算两个目录都要按 slug 取到三个角色——同一份注册，避免两条入口分叉。</summary>
    [Fact]
    public void Contracts_AreRegistered_ForBuildingAndSettlement()
    {
        foreach (var character in new[] { Flowergirl, TownCrier, Oracle })
        {
            var action = NightActions.Default.Find(character);

            Assert.NotNull(action);
            Assert.Equal(character, action!.Character);
            Assert.NotNull(NightActions.Resolutions.Find(character));
        }
    }

    /// <summary>恶魔投过赞成 ⇒ 推演「是」；只读账、不看夜晚时刻的当前角色（R-0037 第 1 条）。</summary>
    [Fact]
    public void FlowergirlPrompt_DemonVoted_SaysYes_EvenIfTheDemonChangedAfterwards()
    {
        var day = DayWithVotes(Vote(seat: 5, character: "fang-gu"), Vote(seat: 2, character: "clockmaker"));
        var state = StateOf((1, LifeState.Alive, "flowergirl", "good"), (5, LifeState.Alive, "clockmaker", "good"));

        var prompt = Prompt(Flowergirl, FlowergirlSeat, state, day);

        Assert.Contains("推演：是", prompt.Context, StringComparison.Ordinal);
        Assert.Empty(prompt.Options);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
    }

    /// <summary>投过的只有非恶魔 ⇒ 推演「否」。</summary>
    [Fact]
    public void FlowergirlPrompt_NoDemonVote_SaysNo()
    {
        var day = DayWithVotes(Vote(seat: 2, character: "clockmaker"), Vote(seat: 3, character: "witch"));

        var prompt = Prompt(Flowergirl, FlowergirlSeat, StateOf(), day);

        Assert.Contains("推演：否", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>恶魔举手后撤回：仍算「是」——记的是举手动作（R-0037 第 2 条）。</summary>
    [Fact]
    public void FlowergirlPrompt_DemonRetracted_StillSaysYes()
    {
        var day = DayWithVotes(
            Vote(seat: 5, character: "no-dashii"),
            Vote(seat: 5, character: "no-dashii", voted: false));

        var prompt = Prompt(Flowergirl, FlowergirlSeat, StateOf(), day);

        Assert.Contains("推演：是", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>有举手但缺角色快照：读不出来就不猜——哪怕同一天还有一次无关举手。</summary>
    [Fact]
    public void FlowergirlPrompt_UnknownSnapshot_SaysUndeterminable()
    {
        var day = DayWithVotes(Vote(seat: 5, character: null), Vote(seat: 2, character: "clockmaker"));

        var prompt = Prompt(Flowergirl, FlowergirlSeat, StateOf(), day);

        Assert.Contains("推演：无法判定", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>还没有白天（首夜直调）⇒ 如实说读不出来。</summary>
    [Fact]
    public void FlowergirlPrompt_WithoutDay_SaysUndeterminable()
    {
        var prompt = Prompt(Flowergirl, FlowergirlSeat, StateOf(), lastDay: null);

        Assert.Contains("推演：无法判定", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>爪牙发起过提名 ⇒ 推演「是」；提名者角色快照之外的当前角色不参与判定。</summary>
    [Fact]
    public void TownCrierPrompt_MinionNominated_SaysYes()
    {
        var day = DayWithNominations(Nomination(nominator: 4, character: "witch"));

        var prompt = Prompt(TownCrier, TownCrierSeat, StateOf(), day);

        Assert.Contains("推演：是", prompt.Context, StringComparison.Ordinal);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
    }

    /// <summary>提名者都不是爪牙 ⇒ 推演「否」；没有提名也是「否」。</summary>
    [Fact]
    public void TownCrierPrompt_TownsfolkNominated_SaysNo()
    {
        var withNomination = Prompt(
            TownCrier,
            TownCrierSeat,
            StateOf(),
            DayWithNominations(Nomination(nominator: 4, character: "clockmaker")));
        var withoutNomination = Prompt(TownCrier, TownCrierSeat, StateOf(), DayWithNominations());

        Assert.Contains("推演：否", withNomination.Context, StringComparison.Ordinal);
        Assert.Contains("推演：否", withoutNomination.Context, StringComparison.Ordinal);
    }

    /// <summary>提名的角色快照缺失且没有确定项 ⇒ 读不出来，不猜。</summary>
    [Fact]
    public void TownCrierPrompt_UnknownSnapshot_SaysUndeterminable()
    {
        var day = DayWithNominations(Nomination(nominator: 4, character: null));

        var prompt = Prompt(TownCrier, TownCrierSeat, StateOf(), day);

        Assert.Contains("推演：无法判定", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>神谕者数当前账里「死亡且邪恶」的席位：含已死的恶魔、不含已死的善良（R-0037 第 3 条）。</summary>
    [Fact]
    public void OraclePrompt_CountsDeadEvilSeats()
    {
        var state = StateOf(
            (1, LifeState.Dead, "no-dashii", "evil"),
            (2, LifeState.Dead, "clockmaker", "good"),
            (3, LifeState.Alive, "witch", "evil"),
            (4, LifeState.Dead, "mutant", "evil"),
            (5, LifeState.Alive, "klutz", "good"));

        var prompt = Prompt(Oracle, OracleSeat, state, DayWithVotes());

        Assert.Contains("推演：2", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>生死 / 阵营未观测 ⇒ 读不出来，不猜。</summary>
    [Fact]
    public void OraclePrompt_UnobservedDimensions_SaysUndeterminable()
    {
        var state = StateOf(
            (1, LifeState.Dead, "no-dashii", "evil"),
            (2, LifeState.Alive, "clockmaker", null));

        var prompt = Prompt(Oracle, OracleSeat, state, DayWithVotes());

        Assert.Contains("推演：无法判定", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>涡流在场：三个角色的提示都注明「必须为假」+ R-0028。</summary>
    [Fact]
    public void Prompts_WithVortox_SayTheInformationMustBeFalse()
    {
        var state = WithVortox(StateOf());
        var day = DayWithVotes(Vote(seat: 5, character: "no-dashii"));

        foreach (var (character, seat) in new[] { (Flowergirl, FlowergirlSeat), (TownCrier, TownCrierSeat), (Oracle, OracleSeat) })
        {
            var prompt = Prompt(character, seat, state, day);

            Assert.Contains("必须为假", prompt.Context, StringComparison.Ordinal);
            Assert.Contains("R-0028", prompt.Context, StringComparison.Ordinal);
        }
    }

    /// <summary>说书人不给内容：显式拒绝，不产出信息事件。</summary>
    [Fact]
    public void Resolve_WithoutStorytellerContent_IsRefused()
    {
        foreach (var (character, seat) in new[] { (Flowergirl, FlowergirlSeat), (TownCrier, TownCrierSeat), (Oracle, OracleSeat) })
        {
            var resolution = NightActions.Resolutions.Find(character)!;

            Assert.Throws<InvalidOperationException>(() => resolution.Resolve(Context(
                character,
                seat,
                StateOf(),
                DayWithVotes(Vote(seat: 5, character: "no-dashii")),
                effective: true,
                decision: null)));
        }
    }

    /// <summary>正常生效：内容原样下发给本人，说明里留平台推演值（只说书人可见）。</summary>
    [Fact]
    public void Resolve_Effective_DeliversContentAndKeepsReadingInNote()
    {
        var resolution = NightActions.Resolutions.Find(Flowergirl)!;

        var events = resolution.Resolve(Context(
            Flowergirl,
            FlowergirlSeat,
            StateOf(),
            DayWithVotes(Vote(seat: 5, character: "no-dashii")),
            effective: true,
            decision: "恶魔参与了投票"));

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Equal(FlowergirlSeat, information.Recipient);
        Assert.Equal(new AbilityId("flowergirl"), information.Ability);
        Assert.Equal("恶魔参与了投票", information.Content);
        Assert.False(information.MayBeFalse);
        Assert.Contains("推演：是", information.Note, StringComparison.Ordinal);
        Assert.Contains("R-0037", information.Note, StringComparison.Ordinal);
    }

    /// <summary>能力未生效（中毒 / 醉酒）：照旧给信息但标「可能错误」，说明沿用生效判定原因。</summary>
    [Fact]
    public void Resolve_Ineffective_MarksInfoAsPossiblyFalse()
    {
        var resolution = NightActions.Resolutions.Find(TownCrier)!;

        var events = resolution.Resolve(Context(
            TownCrier,
            TownCrierSeat,
            StateOf(),
            DayWithNominations(Nomination(nominator: 4, character: "witch")),
            effective: false,
            decision: "说书人给的答案"));

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.True(information.MayBeFalse);
        Assert.Contains("中毒", information.Note, StringComparison.Ordinal);
    }

    /// <summary>涡流在场：哪怕能力正常生效，信息也标「可能为假」并引用 R-0028。</summary>
    [Fact]
    public void Resolve_WithVortox_MarksInfoAsMustBeFalse()
    {
        var resolution = NightActions.Resolutions.Find(Oracle)!;

        var events = resolution.Resolve(Context(
            Oracle,
            OracleSeat,
            WithVortox(StateOf((1, LifeState.Dead, "no-dashii", "evil"))),
            DayWithVotes(),
            effective: true,
            decision: "0"));

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.True(information.MayBeFalse);
        Assert.Contains("涡流", information.Note, StringComparison.Ordinal);
    }

    /// <summary>入槽实时重建：重建来源把 SlotPromptRequest 的白天账接进提示（不依赖计划期快照）。</summary>
    [Fact]
    public void PromptSource_RebuildsWithTheDayLedger()
    {
        var rebuilt = NightActions.Prompts.Rebuild(new SlotPromptRequest
        {
            SlotId = new StepSlotId("flowergirl"),
            Character = Flowergirl,
            Actor = FlowergirlSeat,
            Seats = [FlowergirlSeat, TownCrierSeat, OracleSeat],
            State = StateOf(),
            LastDay = DayWithVotes(Vote(seat: 5, character: "no-dashii")),
        });

        Assert.NotNull(rebuilt);
        Assert.Contains("推演：是", rebuilt!.Context, StringComparison.Ordinal);
    }

    private static ChoicePrompt Prompt(CharacterId character, SeatId actor, GameState state, DayRecord? lastDay) =>
        NightActions.Default.Find(character)!.BuildPrompt(new NightActionContext
        {
            Actor = actor,
            Seats = [FlowergirlSeat, TownCrierSeat, OracleSeat, new SeatId(4), new SeatId(5)],
            State = state,
            LastDay = lastDay,
        });

    private static AbilityResolutionContext Context(
        CharacterId character,
        SeatId actor,
        GameState state,
        DayRecord? lastDay,
        bool effective,
        string? decision) => new()
        {
            SlotId = new StepSlotId(character.Value),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = actor,
            ActorCharacter = character,
            ActorOwnCharacter = character,
            Seats = [FlowergirlSeat, TownCrierSeat, OracleSeat, new SeatId(4), new SeatId(5)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
                Note = effective ? null : "来源中毒：能力未生效",
            },
            Decision = decision,
            DaysStarted = 1,
            LastDay = lastDay,
        };

    private static DayRecord DayWithVotes(params DayVoteAttempt[] attempts) => new()
    {
        DayNumber = 1,
        Status = DayStatus.Closed,
        VoteAttempts = attempts,
    };

    private static DayRecord DayWithNominations(params NominationRecord[] nominations) => new()
    {
        DayNumber = 1,
        Status = DayStatus.Closed,
        Nominations = nominations,
    };

    private static DayVoteAttempt Vote(int seat, string? character, bool voted = true) => new()
    {
        NominationIndex = 1,
        Voter = new SeatId(seat),
        VoterCharacter = character is null ? null : new CharacterId(character),
        Voted = voted,
    };

    private static NominationRecord Nomination(int nominator, string? character) => new()
    {
        Index = 1,
        Nominator = new SeatId(nominator),
        Nominee = new SeatId(5),
        NominatorCharacter = character is null ? null : new CharacterId(character),
        Status = NominationStatus.Counted,
    };

    private static GameState StateOf(params (int Seat, LifeState Life, string Character, string? Alignment)[] seats)
    {
        var events = seats
            .Select(item => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(item.Seat),
                Life = item.Life,
                Character = new CharacterId(item.Character),
                Alignment = item.Alignment switch
                {
                    "evil" => Alignment.Evil,
                    "good" => Alignment.Good,
                    _ => null,
                },
                Reason = "test.retrospective-info",
            })
            .ToArray();

        return GameStateMachine.Fold(events);
    }

    private static GameState WithVortox(GameState state) => state with
    {
        Seats =
        [
            .. state.Seats,
            new SeatStateEntry
            {
                Seat = new SeatId(9),
                Character = new StateFact<CharacterId> { Value = Vortox, Reason = "测试：涡流在场" },
                Life = new StateFact<LifeState> { Value = LifeState.Alive, Reason = "测试：涡流在场" },
            },
        ],
    };
}
