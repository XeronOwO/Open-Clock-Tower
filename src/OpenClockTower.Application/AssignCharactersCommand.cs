namespace OpenClockTower.Application;

/// <summary>
/// 开局分配（仅首个阶段开始前）：为席位记录角色与初始生死。
/// </summary>
/// <remarks>
/// <para>
/// 角色是六维度之一，写在**事件流**里（决策 D-0017）：每席产出一条 <c>SeatStateChangedEvent</c>，
/// 折进状态账（D-0015）；不写进会话票据（<see cref="GameSetup"/> 只承载"谁拿哪张票"）。
/// </para>
/// <para>
/// 初始生死 = 存活一并记录，依据：百科《规则概要》· 2026-10-01 抓取 · 一-4「准备城镇广场」
/// （开局给每名玩家放置生命标记）；百科《重要细节》· 2026-10-01 抓取 · 三-1
/// （死亡来自处决或角色能力，二者都发生在游戏开始之后，首夜之前没有死亡来源）。
/// 这是**补全初始条件**，不是运行期把两个维度耦合：运行期的状态观测仍是一次只报本次变化的维度
/// （见 <c>docs/standard/rulings.md</c> R-0015）。
/// </para>
/// </remarks>
public sealed record AssignCharactersCommand : GameCommand
{
    /// <summary>本批分配的席位与角色；允许分成多批（开局完整性在开夜时由建表器校验）。</summary>
    public required IReadOnlyList<SeatCharacterAssignment> Assignments { get; init; }
}
