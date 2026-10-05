namespace OpenClockTower.Kernel;

/// <summary>
/// 待定死亡确认时**先于死亡**落下的「保留能力」载荷（亡骨魔杀死的爪牙）。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《亡骨魔》· 2026-10-01 抓取 · 规则细节 13 / 19——被选择的玩家是爪牙时，
/// 为他放置「死亡」与「保留能力」提示标记；百科《死后能力保留》· 2026-10-01 抓取 · 能力简介——
/// 这类能力「生效与否不关注玩家的生死状态」。
/// </para>
/// <para>
/// 它为什么挂在待定死亡上：麻脸巫婆之夜的死亡裁量窗口把当夜所有恶魔击杀都变成待定死亡
/// （<c>docs/standard/rulings.md</c> R-0030），说书人**确认**时这条载荷按普通夜晚同一条路径落格，
/// **阻止**时它整条不产生——两条路径的结果因此一致（口径见 R-0056）。
/// </para>
/// <para>
/// 载荷带上整条窗口效果（而不是只带"要落一条窗口"）：效果标识与形状由规则层派生，
/// 内核只负责在死亡**之前**把它落进事件流——顺序在这里有语义：死亡折叠时据此判定
/// 「没有失去能力」，该爪牙名下既有的持续型效果与疯狂要求才不会被误终止。
/// </para>
/// </remarks>
public sealed record DeferredRetention
{
    /// <summary>确认时立刻落下的「保留能力」窗口效果（作用对象 = 被杀死并保留能力的爪牙）。</summary>
    public required PersistentEffect RetainEffect { get; init; }

    /// <summary>说书人选择的中毒侧；null = 场上没有镇民可中毒。</summary>
    public SeatRingDirection? Side { get; init; }
}
