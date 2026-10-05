namespace OpenClockTower.Kernel;

/// <summary>
/// 槽位追加：把一格**新的**行动槽位插进尚未走进的计划。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="SlotActivatedEvent"/> 的分工：那条是"计划里本来就有这一格，现在决定唤醒谁"，
/// 本事件是"计划里**根本没有**这一格"——夜里获得一项「在你的首个夜晚」能力时就属于后者：
/// 那类角色的行动格只出现在首夜顺序表上，其他夜晚表里没有它的位置，因此必须**追加**。
/// </para>
/// <para>
/// 依据（均为百科 · 2026-10-01 抓取）：《重要细节》· 七——「如果新的能力原本只在游戏的首个夜晚产生效果，
/// 那么它会在角色变化的当晚产生效果」「如果带有『在你的首个夜晚』角色在游戏中途被创造，
/// 这些角色会尽可能快地进行进场能力的效果结算，因为他们的能力不会在夜晚顺序表的『其他夜晚』部分出现。
/// 但仍需注意，绝大部分获取信息的能力应该在所有会造成死亡的效果之后结算」；
/// 《获得能力》· 能力简介——非首个夜晚获得进场能力时「在夜晚顺序表中……插入结算这些效果，
/// 唤醒该玩家并触发相应的能力」，时机晚于该点时「立即生效并进行结算」。
/// 平台口径（追加位怎么算、多名持有者的顺序）见 <c>docs/standard/rulings.md</c> R-0055。
/// </para>
/// <para>
/// 只允许插进**尚未进入**的位置（<c>Index &gt; 当前槽位下标</c>）：背后的格子已经走过，
/// 回填会让事件历史与投影分叉（《钟楼谜团隐性规则汇总》§6「过时不候」的同族姿态）。
/// </para>
/// </remarks>
public sealed record SlotInsertedEvent : GameEvent
{
    /// <summary>插入位置：新槽位插在该下标**之前**。</summary>
    public required int Index { get; init; }

    /// <summary>
    /// 被插进去的槽位（**数据形态**：提示随事件走，与 <see cref="StepPlan"/> 里的槽位同一种记录）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="SlotActivatedEvent"/> 的字段是同一副姿态：行为对象（<c>INightAction</c>）过不了
    /// 事件流的 JSON 往返，因此契约在结算时把提示构造好，随事件落盘。
    /// </remarks>
    public required StepSlot Slot { get; init; }
}
