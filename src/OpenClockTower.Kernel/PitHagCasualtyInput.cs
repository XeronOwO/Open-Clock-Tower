namespace OpenClockTower.Kernel;

/// <summary>
/// 麻脸巫婆之夜的「追加死亡」输入：说书人在窗口期内主动让某名玩家死亡。
/// </summary>
/// <remarks>
/// 依据：百科《麻脸巫婆》· 2026-10-01 抓取 · 规则细节 1——「说书人能够自由决定是否让某名玩家死亡」
/// 「说书人造成的死亡视为麻脸巫婆造成，因此不会触发贤者、唱诗男孩等角色的能力」；
/// 窗口关闭后该输入被拒（R-0030 第 4 条）。
/// </remarks>
public sealed record PitHagCasualtyInput : StepMachineInput
{
    /// <summary>被说书人判定死亡的席位（可以是尚未行动的恶魔）。</summary>
    public required SeatId Target { get; init; }

    /// <summary>说书人的说明（进死亡事实的原因与审计；可空）。</summary>
    public string? Note { get; init; }
}
