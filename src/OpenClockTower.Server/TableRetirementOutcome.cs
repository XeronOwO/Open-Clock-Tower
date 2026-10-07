using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 清扫对**一张桌**的判定与动作（M5 / G-A6-5）：为什么留、为什么删、删掉了什么。
/// </summary>
/// <remarks>
/// <see cref="Reason"/> 是给人看的（维护命令直接打印它）：回收是不可逆动作，
/// "为什么是这一张"必须当场说得出来，而不是让人去猜阈值。
/// </remarks>
/// <param name="GameId">桌标识。</param>
/// <param name="Name">桌名（未命名为空串）。</param>
/// <param name="Verdict">判定结果。</param>
/// <param name="Reason">一句话说明（含空闲时长与保留期，用于报告与日志）。</param>
/// <param name="IdleFor">有依据时空闲了多久；无依据时为 null。</param>
/// <param name="Purged">真的删了时的逐表行数；只报告（`apply` 为假）时为 null。</param>
public sealed record TableRetirementOutcome(
    GameId GameId,
    string Name,
    TableRetirementVerdict Verdict,
    string Reason,
    TimeSpan? IdleFor,
    TablePurgeResult? Purged);
