using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 「角色今夜刚被创造出来 → 激活它尚未进入的那一格」的规则回归
/// （依据：百科《夜晚行动顺序一览》· 2026-10-01 抓取 · 麻脸巫婆条；平台口径 rulings.md R-0030 第 6 条）。
/// </summary>
public sealed class NightSlotActivationTests
{
    /// <summary>有契约、且那一格还没走到 → 产激活事件，行动者与契约都对得上。</summary>
    [Fact]
    public void PendingSlotWithContract_IsActivated()
    {
        var plan = Plan(
            "sv:night-2",
            StepSlot.Beat(new StepSlotId("dusk")),
            StepSlot.Empty(new StepSlotId("vortox"), new CharacterId("vortox")));

        var activation = NightSlotActivation.Plan(
            plan,
            slotIndex: 0,
            actor: new SeatId(2),
            character: new CharacterId("vortox"),
            state: GameState.Empty,
            seats: [new SeatId(1), new SeatId(2), new SeatId(3)],
            catalog: NightActions.Default);

        Assert.NotNull(activation);
        Assert.Equal(1, activation!.SlotIndex);
        Assert.Equal(new StepSlotId("vortox"), activation.SlotId);
        Assert.Equal(new SeatId(2), activation.Actor);
        Assert.NotEmpty(activation.Prompt.Options);
        Assert.Contains(
            activation.Dependencies,
            dependency => dependency.Seat == new SeatId(2) && dependency.RequiredCharacter == new CharacterId("vortox"));
    }

    /// <summary>那一格已经走过（过时不候）→ 不激活。</summary>
    [Fact]
    public void SlotAlreadyPassed_IsNotActivated()
    {
        var plan = Plan(
            "sv:night-2",
            StepSlot.Beat(new StepSlotId("dusk")),
            StepSlot.Empty(new StepSlotId("vortox"), new CharacterId("vortox")));

        var activation = NightSlotActivation.Plan(
            plan,
            slotIndex: 1,
            actor: new SeatId(2),
            character: new CharacterId("vortox"),
            state: GameState.Empty,
            seats: [new SeatId(1), new SeatId(2)],
            catalog: NightActions.Default);

        Assert.Null(activation);
    }

    /// <summary>角色在夜晚顺序表上、但契约还没实现 → 不激活（进入那一格时由步骤机显式阻塞）。</summary>
    [Fact]
    public void ContractMissing_IsNotActivated()
    {
        var plan = Plan(
            "sv:night-2",
            StepSlot.Empty(new StepSlotId("sage"), new CharacterId("sage")));

        var activation = NightSlotActivation.Plan(
            plan,
            slotIndex: -1,
            actor: new SeatId(1),
            character: new CharacterId("sage"),
            state: GameState.Empty,
            seats: [new SeatId(1), new SeatId(2)],
            catalog: NightActions.Default);

        Assert.Null(activation);
    }

    /// <summary>没有计划（阶段外结算 / 夹具）→ 不激活。</summary>
    [Fact]
    public void NoPlan_IsNotActivated()
    {
        var activation = NightSlotActivation.Plan(
            plan: null,
            slotIndex: 0,
            actor: new SeatId(1),
            character: new CharacterId("vortox"),
            state: GameState.Empty,
            seats: [new SeatId(1)],
            catalog: NightActions.Default);

        Assert.Null(activation);
    }

    private static StepPlan Plan(string label, params StepSlot[] slots) => new()
    {
        Label = label,
        Phase = GamePhase.OtherNight,
        Slots = slots,
    };
}
