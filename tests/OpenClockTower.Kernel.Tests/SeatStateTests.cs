using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 席位状态不变量：六个维度相互独立。
/// </summary>
/// <remarks>
/// 每条断言都对应一条百科出处，不是凭空造的断言。
/// 依据：百科《重要细节》三——"状态与玩家绑定，而不与角色绑定"，
/// "在游戏开始后，玩家的不同状态之间相互独立，互不干涉"。
/// </remarks>
public sealed class SeatStateTests
{
    /// <summary>醉酒与中毒互不抵消：可以同时成立。</summary>
    /// <remarks>百科《重要细节》三-3：「醉酒与中毒状态不会互相抵消。」</remarks>
    [Fact]
    public void DrunkAndPoisoned_CanHoldAtTheSameTime()
    {
        var seat = Seat(drunk: DrunkState.Drunk, poison: PoisonState.Poisoned);

        Assert.Equal(DrunkState.Drunk, seat.Drunk);
        Assert.Equal(PoisonState.Poisoned, seat.Poison);
    }

    /// <summary>死亡不解除醉酒与中毒。</summary>
    /// <remarks>百科《重要细节》三-3：「无论玩家存活还是死亡，玩家都能醉酒或中毒。」</remarks>
    [Fact]
    public void DeadSeat_CanStillBeDrunkAndPoisoned()
    {
        var seat = Seat(
            life: LifeState.Dead,
            drunk: DrunkState.Drunk,
            poison: PoisonState.Poisoned);

        Assert.Equal(LifeState.Dead, seat.Life);
        Assert.Equal(DrunkState.Drunk, seat.Drunk);
        Assert.Equal(PoisonState.Poisoned, seat.Poison);
    }

    /// <summary>改变角色不会解除醉酒。</summary>
    /// <remarks>
    /// 百科《重要细节》三：「因其他角色能力而醉酒的玩家角色发生了变化，
    /// 不会让玩家因此解除醉酒。」
    /// </remarks>
    [Fact]
    public void ChangingCharacter_DoesNotClearDrunk()
    {
        var before = Seat(drunk: DrunkState.Drunk);

        var after = before with { Character = new CharacterId("snake-charmer") };

        Assert.Equal(new CharacterId("snake-charmer"), after.Character);
        Assert.Equal(DrunkState.Drunk, after.Drunk);
    }

    /// <summary>改变角色不会解除中毒，也不会改变阵营。</summary>
    /// <remarks>
    /// 百科《重要细节》三：「如果一名中毒的玩家与其他玩家交换了角色，他仍然处于中毒状态。」
    /// </remarks>
    [Fact]
    public void ChangingCharacter_DoesNotClearPoisonOrAlignment()
    {
        var before = Seat(poison: PoisonState.Poisoned, alignment: Alignment.Evil);

        var after = before with { Character = new CharacterId("barber") };

        Assert.Equal(PoisonState.Poisoned, after.Poison);
        Assert.Equal(Alignment.Evil, after.Alignment);
    }

    /// <summary>阵营与角色相互独立：改变阵营不影响角色、醉酒等其它维度。</summary>
    /// <remarks>
    /// 百科《重要细节》三-2：「如果一名玩家改变了阵营，他的角色会保持不变，反之亦然。」
    /// </remarks>
    [Fact]
    public void ChangingAlignment_LeavesCharacterAndImpairmentsUntouched()
    {
        var before = Seat(
            character: "imp",
            alignment: Alignment.Evil,
            drunk: DrunkState.Drunk);

        var after = before with { Alignment = Alignment.Good };

        Assert.Equal(Alignment.Good, after.Alignment);
        Assert.Equal(before.Character, after.Character);
        Assert.Equal(DrunkState.Drunk, after.Drunk);
    }

    /// <summary>改变生死不影响角色、阵营、醉酒与中毒——死亡不是"重置"。</summary>
    /// <remarks>百科《重要细节》三：各状态互不干涉。</remarks>
    [Fact]
    public void ChangingLife_LeavesEveryOtherDimensionUntouched()
    {
        var before = Seat(drunk: DrunkState.Drunk, poison: PoisonState.Poisoned);

        var after = before with { Life = LifeState.Dead };

        Assert.Equal(LifeState.Dead, after.Life);
        Assert.Equal(before.Character, after.Character);
        Assert.Equal(before.Alignment, after.Alignment);
        Assert.Equal(DrunkState.Drunk, after.Drunk);
        Assert.Equal(PoisonState.Poisoned, after.Poison);
    }

    private static SeatState Seat(
        string character = "clockmaker",
        Alignment alignment = Alignment.Good,
        LifeState life = LifeState.Alive,
        DrunkState drunk = DrunkState.Sober,
        PoisonState poison = PoisonState.Healthy) =>
        new()
        {
            Character = new CharacterId(character),
            Alignment = alignment,
            Life = life,
            Drunk = drunk,
            Poison = poison,
        };
}
