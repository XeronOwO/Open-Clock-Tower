using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 建表请求：把「第几夜、哪套口径、本局有哪些席位、当前状态账、有哪些行动契约」一次给全。
/// </summary>
/// <remarks>
/// 席位名单来自会话信息（<c>GameSetup</c>），不是客户端输入；状态账是事件流的折叠结果（D-0015）。
/// 建表器是**纯函数**：相同请求必产出相同计划（D-0008）。
/// </remarks>
public sealed record NightPlanRequest
{
    /// <summary>夜晚序号：1 = 首夜，其余 = 其他夜晚。</summary>
    public required int NightNumber { get; init; }

    /// <summary>夜晚顺序口径；选择结果记录进 <see cref="StepPlan.Variant"/>（R-0014）。</summary>
    public required NightOrderVariant Variant { get; init; }

    /// <summary>本局完整席位名单（升序）；决定「其他玩家」这类目标集合。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>状态账（角色 / 生死等已知态）。</summary>
    public required GameState State { get; init; }

    /// <summary>
    /// 最近一个白天的账目（首夜 / 还没有白天时为 null）：回溯型信息能力的计划期提示按它推演。
    /// </summary>
    /// <remarks>入槽时同一条记录经 <see cref="SlotPromptRequest.LastDay"/> 再送一次（实时重建）。</remarks>
    public DayRecord? LastDay { get; init; }

    /// <summary>角色夜间行动契约目录。</summary>
    public required INightActionCatalog Actions { get; init; }
}
