namespace OpenClockTower.Kernel;

/// <summary>
/// 槽位激活：把「这个角色此刻才被创造出来」翻译成「这一格待会儿要唤醒谁」。
/// </summary>
/// <remarks>
/// <para>
/// 建表发生在阶段开始之前，那时不在场的角色只有空槽位（D-0013 §1：空槽位照样走配额、不缩短夜晚）。
/// 角色变更能力（麻脸巫婆等）在夜里把某个角色创造出来之后，该角色**尚未进入**的槽位必须被激活——
/// 依据：百科《夜晚行动顺序一览》· 2026-10-01 抓取 · 麻脸巫婆条「否则，就需要唤醒这名玩家」
/// 「例如玩家变成了恶魔，则在恶魔行动时一并唤醒，通知角色变化并让他执行相应行动」。
/// </para>
/// <para>
/// 只允许激活**尚未进入**的槽位（<c>SlotIndex &gt; state.SlotIndex</c>）：时机已过的槽位照旧空转，
/// 与《钟楼谜团隐性规则汇总》§6「过时不候」一致。计划随事件流走，重放得到同一份被激活的计划
/// （D-0010：事件是唯一事实来源）。
/// </para>
/// </remarks>
public sealed record SlotActivatedEvent : GameEvent
{
    /// <summary>被激活的槽位下标。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>被激活的槽位标识（与计划里的那一格核对，防止错位）。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>行动者席位（此刻持有该角色的那一席）。</summary>
    public required SeatId Actor { get; init; }

    /// <summary>
    /// 给行动者的选择契约：由规则层在激活时构造（计划里只存数据，
    /// 行为对象过不了事件流的 JSON 往返）。
    /// </summary>
    public required ChoicePrompt Prompt { get; init; }

    /// <summary>座位依赖：任一不满足即自动作废该请求。</summary>
    public IReadOnlyList<SeatDependency> Dependencies { get; init; } = [];
}
