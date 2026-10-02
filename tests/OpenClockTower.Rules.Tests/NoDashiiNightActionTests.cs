using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 诺-达鲺的夜间契约（走引擎使用的公开目录检索，与生产同一批实现）：
/// 合法集合 = 全体席位（《重要细节》三-1：可选自己与已死亡玩家）；生效才击杀；已死者不再死亡。
/// </summary>
public sealed class NoDashiiNightActionTests
{
    private static readonly INightAction Prompt =
        NightActions.Default.Find(new CharacterId("no-dashii"))!;

    private static readonly IAbilityResolution Resolution =
        NightActions.Resolutions.Find(new CharacterId("no-dashii"))!;

    [Fact]
    public void Prompt_OffersEverySeatIncludingSelf()
    {
        var prompt = Prompt.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(1),
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3)],
            State = GameState.Empty,
        });

        Assert.Equal(["seat:1", "seat:2", "seat:3"], prompt.Options.Select(option => option.Value));
    }

    [Fact]
    public void Resolve_KillsLivingTargetWithAttribution()
    {
        var events = Resolution.Resolve(Context(choice: "seat:2", effective: true, LifeState.Alive));

        var effect = Assert.Single(events.OfType<InstantaneousEffectAppliedEvent>());
        Assert.Equal(new SeatId(1), effect.Effect.Source);
        Assert.Equal(new SeatId(2), effect.Effect.Target);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(LifeState.Dead, change.Life);
        Assert.Equal(new SeatId(1), change.CausedBy);
        Assert.Equal(effect.Effect.Id, change.EffectId);
    }

    [Fact]
    public void Resolve_IneffectiveAbilityKillsNobody()
    {
        var events = Resolution.Resolve(Context(choice: "seat:2", effective: false, LifeState.Alive));

        Assert.Empty(events);
    }

    [Fact]
    public void Resolve_DeadTargetDoesNotDieAgain()
    {
        var events = Resolution.Resolve(Context(choice: "seat:2", effective: true, LifeState.Dead));

        Assert.Empty(events);
    }

    [Fact]
    public void Resolve_UnobservedTargetLife_IsRefused()
    {
        var context = Context(choice: "seat:2", effective: true, LifeState.Alive) with
        {
            State = GameStateMachine.Fold(
            [
                new SeatStateChangedEvent
                {
                    Seat = new SeatId(2),
                    Character = new CharacterId("dreamer"),
                    Reason = "只观测到角色",
                },
            ]),
        };

        Assert.Throws<InvalidOperationException>(() => Resolution.Resolve(context));
    }

    [Fact]
    public void Resolve_IllegalChoice_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(
            () => Resolution.Resolve(Context(choice: "随便", effective: true, LifeState.Alive)));
    }

    private static AbilityResolutionContext Context(string choice, bool effective, LifeState targetLife) => new()
    {
        SlotId = new StepSlotId("no-dashii"),
        PlanLabel = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Actor = new SeatId(1),
        ActorCharacter = new CharacterId("no-dashii"),
        ActorOwnCharacter = new CharacterId("no-dashii"),
        Seats = [new SeatId(1), new SeatId(2)],
        State = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Character = new CharacterId("dreamer"),
                Life = targetLife,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            },
        ]),
        Outcome = new AbilityOutcome
        {
            Effective = effective,
            Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
        },
        Choice = choice,
        DaysStarted = 0,
    };
}
