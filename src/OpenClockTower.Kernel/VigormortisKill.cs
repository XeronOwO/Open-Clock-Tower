namespace OpenClockTower.Kernel;

/// <summary>
/// 亡骨魔的一次「杀死爪牙」事实：哪位亡骨魔杀死了哪位爪牙，以及说书人为他选的中毒侧。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《亡骨魔》· 2026-10-01 抓取 · 规则细节 13–14、19、22——被选择的玩家是爪牙时，
/// 为他放置「死亡」与「保留能力」提示标记；「距离爪牙顺时针或逆时针最近的镇民玩家中毒」
/// 「顺时针和逆时针分别判断最近的一个镇民，并取这两个镇民的其中之一」。
/// </para>
/// <para>
/// 这里记的是**说书人的选择**（哪一侧），不是此刻中毒的那个席位：百科同页规则细节 23 明确
/// 「一旦标记了此『中毒』标记的玩家不再是放置了『保留能力』标记的邻近的镇民玩家之一，
/// 就需要移动『中毒』标记至**同一侧**的满足条件的玩家角色标记旁（不会因此替换到另一侧去）」——
/// 目标由方向 + 当前座次重算，侧不变。保留能力与中毒两条效果都由常驻来源按本条记录派生，
/// 契约只负责在击杀当场把这条事实记下来（口径见 <c>docs/standard/rulings.md</c> R-0056）。
/// </para>
/// </remarks>
public sealed record VigormortisKill
{
    /// <summary>发起击杀的亡骨魔席位。</summary>
    public required SeatId Demon { get; init; }

    /// <summary>被杀死并保留能力的爪牙席位。</summary>
    public required SeatId Minion { get; init; }

    /// <summary>
    /// 说书人选择的中毒侧；null = 场上没有镇民可中毒（保留下能力，但没有中毒效果）。
    /// </summary>
    public SeatRingDirection? Side { get; init; }
}
