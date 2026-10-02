using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人在麻脸巫婆之夜追加死亡：让某名玩家死亡，归因为麻脸巫婆。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0030 第 4 条与百科《麻脸巫婆》· 2026-10-01 抓取 · 规则细节 1：
/// 「说书人能够自由决定是否让某名玩家死亡」「说书人造成的死亡视为麻脸巫婆造成」。
/// 窗口是否存在、目标是否已死由内核判定；这里只做身份与席位形状检查。
/// </remarks>
public sealed record PitHagCasualtyCommand : GameCommand
{
    /// <summary>被说书人判定死亡的席位（可以是尚未行动的恶魔）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>说书人的说明（可选，进死亡事实的原因与审计）。</summary>
    public string? Note { get; init; }
}
