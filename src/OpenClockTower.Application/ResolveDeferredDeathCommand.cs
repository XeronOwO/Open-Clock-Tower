using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人裁定一条待定死亡：确认（该玩家死亡）或阻止（免死）——麻脸巫婆之夜。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0030 第 2 条与百科《免死》· 2026-10-01 抓取 · 角色列表：
/// 「麻脸巫婆在创造恶魔的夜晚，说书人能让原本被恶魔攻击且会死亡的玩家免死」。
/// 是否存在待定死亡、窗口是否还开着由内核判定。
/// </remarks>
public sealed record ResolveDeferredDeathCommand : GameCommand
{
    /// <summary>待定死亡的目标席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>true = 确认死亡；false = 阻止死亡。</summary>
    public required bool Killed { get; init; }

    /// <summary>说书人的说明（可选，进裁定事件）。</summary>
    public string? Note { get; init; }
}
