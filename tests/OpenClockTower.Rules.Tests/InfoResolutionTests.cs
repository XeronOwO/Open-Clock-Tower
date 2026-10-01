using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 信息类契约（筑梦师 / 钟表匠）：信息由说书人裁定，平台只记录与提示、不判定真假；
/// 能力未生效时照旧给信息但标「可能错误」——该标记只说书人可见。
/// </summary>
public sealed class InfoResolutionTests
{
    private static readonly INightAction DreamerPrompt =
        NightActions.Default.Find(new CharacterId("dreamer"))!;

    private static readonly IAbilityResolution Dreamer =
        NightActions.Resolutions.Find(new CharacterId("dreamer"))!;

    private static readonly IAbilityResolution Clockmaker =
        NightActions.Resolutions.Find(new CharacterId("clockmaker"))!;

    [Fact]
    public void DreamerPrompt_OffersEveryOtherSeat()
    {
        var prompt = DreamerPrompt.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(1),
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3)],
            State = GameState.Empty,
        });

        Assert.Equal(["seat:2", "seat:3"], prompt.Options.Select(option => option.Value));
    }

    [Fact]
    public void DreamerEffective_OffersOppositeTypeCandidates()
    {
        var prompt = Dreamer.BuildPostChoiceDecision(Context(choice: "seat:3", effective: true, targetCharacter: "clockmaker"));

        Assert.NotNull(prompt);
        Assert.Equal(8, prompt!.Options.Count);
        Assert.All(prompt.Options, option =>
            Assert.Contains(new CharacterId(option.Value), SectsAndVioletsRoster.OfType(CharacterType.Minion)
                .Concat(SectsAndVioletsRoster.OfType(CharacterType.Demon))));
        Assert.Contains(prompt.Options, option => option.Preview == "洗脑师");
    }

    [Fact]
    public void DreamerEffective_ComposesTrueAndChosenPair()
    {
        var events = Dreamer.Resolve(Context(choice: "seat:3", effective: true, targetCharacter: "clockmaker") with
        {
            Decision = "cerenovus",
        });

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Equal(new SeatId(2), information.Recipient);
        Assert.Contains("钟表匠", information.Content, StringComparison.Ordinal);
        Assert.Contains("洗脑师", information.Content, StringComparison.Ordinal);
        Assert.True(information.MayBeFalse);
    }

    [Fact]
    public void DreamerEffective_RejectsSameSideCandidate()
    {
        var context = Context(choice: "seat:3", effective: true, targetCharacter: "clockmaker") with
        {
            Decision = "dreamer",
        };

        Assert.Throws<InvalidOperationException>(() => Dreamer.Resolve(context));
    }

    [Fact]
    public void DreamerIneffective_TakesFreeFormInfoFromStoryteller()
    {
        var postChoice = Dreamer.BuildPostChoiceDecision(
            Context(choice: "seat:3", effective: false, targetCharacter: "clockmaker"));

        Assert.NotNull(postChoice);
        Assert.Empty(postChoice!.Options);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, postChoice.OnNoOption);

        var events = Dreamer.Resolve(
            Context(choice: "seat:3", effective: false, targetCharacter: "clockmaker") with
            {
                Decision = "3 号玩家是「钟表匠」。",
            });

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Equal("3 号玩家是「钟表匠」。", information.Content);
        Assert.True(information.MayBeFalse);
        Assert.Contains("中毒", information.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ClockmakerEffective_MarksStorytellerInfoAsReliable()
    {
        var events = Clockmaker.Resolve(Context(choice: null, effective: true, targetCharacter: "clockmaker") with
        {
            Decision = "恶魔与最近的爪牙之间隔着 1 名玩家。",
        });

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Equal(new SeatId(2), information.Recipient);
        Assert.False(information.MayBeFalse);
    }

    [Fact]
    public void ClockmakerIneffective_MarksInfoAsPossiblyFalse()
    {
        var events = Clockmaker.Resolve(Context(choice: null, effective: false, targetCharacter: "clockmaker") with
        {
            Decision = "你得到的信息由我说书人裁定。",
        });

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.True(information.MayBeFalse);
    }

    [Fact]
    public void ClockmakerWithoutDecision_IsRefused()
    {
        var context = Context(choice: null, effective: true, targetCharacter: "clockmaker");

        Assert.Throws<InvalidOperationException>(() => Clockmaker.Resolve(context));
    }

    private static AbilityResolutionContext Context(string? choice, bool effective, string targetCharacter) => new()
    {
        SlotId = new StepSlotId("information"),
        PlanLabel = "sv:night-1",
        Phase = GamePhase.FirstNight,
        Actor = new SeatId(2),
        ActorCharacter = new CharacterId("dreamer"),
        Seats = [new SeatId(1), new SeatId(2), new SeatId(3)],
        State = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = new SeatId(3),
                Character = new CharacterId(targetCharacter),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            },
        ]),
        Outcome = new AbilityOutcome
        {
            Effective = effective,
            Malfunction = effective ? null : MalfunctionKind.Poisoned,
            Note = effective ? null : "来源中毒：能力未生效",
        },
        Choice = choice,
    };
}
