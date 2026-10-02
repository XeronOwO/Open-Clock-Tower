namespace OpenClockTower.Kernel;

/// <summary>
/// 槽位绑定：把「这个角色此刻才被创造出来 / 换了持有者」翻译成「这一格待会儿要唤醒谁」。
/// </summary>
/// <remarks>
/// <para>
/// 建表发生在阶段开始之前，那时不在场的角色只有空槽位（D-0013 §1：空槽位照样走配额、不缩短夜晚）。
/// 角色变更能力在夜里把某个角色**创造出来**之后，该角色尚未进入的槽位必须被激活——
/// 依据：百科《夜晚行动顺序一览》· 2026-10-01 抓取 · 麻脸巫婆条「否则，就需要唤醒这名玩家」
/// 「例如玩家变成了恶魔，则在恶魔行动时一并唤醒，通知角色变化并让他执行相应行动」。
/// </para>
/// <para>
/// 角色**换手**（舞蛇人交换角色等）时，角色仍在场但持有者变了：原本绑给旧持有者的行动槽位
/// 同样在这里重新绑定给新持有者（行动者与提示按新持有者重建）——依据《重要细节》· 2026-10-01 抓取 ·
/// 七「交换了角色……视为他获得了一个新的角色」「立即获得这个新角色的能力」，平台口径见
/// <c>docs/standard/rulings.md</c> R-0032。
/// </para>
/// <para>
/// 只允许处理**尚未进入**的槽位（<c>SlotIndex &gt; state.SlotIndex</c>）：时机已过的槽位照旧空转，
/// 与《钟楼谜团隐性规则汇总》§6「过时不候」一致。计划随事件流走，重放得到同一份被绑定的计划
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

    /// <summary>
    /// 行动者**本人**的角色；null = 与槽位角色相同（普通激活 / 换手重绑）。
    /// </summary>
    /// <remarks>
    /// 哲学家代行"被获得角色"的能力时，行动者（哲学家）与槽位角色（被获得角色）不再是同一个：
    /// 折叠时据它构造代行槽位（<see cref="StepSlot.GrantedAction"/>），
    /// 这样进入那一格时按行动者本人的角色确认他还站得住。口径见
    /// <c>docs/standard/rulings.md</c> R-0036。
    /// </remarks>
    public CharacterId? ActorCharacter { get; init; }
}
