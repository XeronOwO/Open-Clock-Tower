using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>构造测试用席位状态的共享助手。</summary>
internal static class SeatFixture
{
    /// <summary>按给定维度构造一个席位；未指定的维度取默认值。</summary>
    internal static SeatState Seat(
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
