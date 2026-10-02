namespace OpenClockTower.Kernel;

/// <summary>
/// 一条待定死亡被裁定：<see cref="Killed"/> = true 时确认死亡（随裁定产出死亡事实），
/// false = 说书人阻止了这次死亡（百科《免死》· 2026-10-01 抓取 · 角色列表：
/// 「麻脸巫婆在创造恶魔的夜晚，说书人能让原本被恶魔攻击且会死亡的玩家免死」）。
/// </summary>
public sealed record DeferredDeathResolvedEvent : GameEvent
{
    /// <summary>被裁定（或被窗口收口）的席位。</summary>
    public required SeatId Target { get; init; }

    /// <summary>true = 确认死亡；false = 阻止死亡。</summary>
    public required bool Killed { get; init; }

    /// <summary>裁定说明（窗口收口时写明「未裁定 → 按默认结果生效」）。</summary>
    public required string Note { get; init; }
}
