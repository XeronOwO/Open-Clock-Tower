using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>行动契约的求值上下文：谁在行动、本局有哪些席位、当前状态账。</summary>
/// <remarks>
/// 席位名单来自会话信息（<c>GameSetup</c>），不是客户端输入——契约按它算"其他玩家"这类目标集合；
/// 状态账用于需要按当前态过滤目标的契约。
/// </remarks>
public sealed record NightActionContext
{
    /// <summary>本次行动者席位。</summary>
    public required SeatId Actor { get; init; }

    /// <summary>本局完整席位名单（升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>当前状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>
    /// 最近一个白天的账目（首夜 / 还没有白天时为 null）：回溯型信息能力（卖花女孩 / 城镇公告员）
    /// 要按它推演"今天发生过什么"（R-0037）。
    /// </summary>
    public DayRecord? LastDay { get; init; }
}
