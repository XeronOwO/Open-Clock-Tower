using System.Globalization;

namespace OpenClockTower.Kernel;

/// <summary>
/// 玩家席位标识：一局游戏内稳定不变，与座位号一致（从 1 开始）。
/// </summary>
/// <remarks>
/// 状态属于玩家（席位），不属于角色：角色或阵营变化不会改变它。
/// 依据：百科《重要细节》三-3——「状态与玩家绑定，而不与角色绑定」。
/// </remarks>
/// <param name="Value">席位号。</param>
public readonly record struct SeatId(int Value)
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
