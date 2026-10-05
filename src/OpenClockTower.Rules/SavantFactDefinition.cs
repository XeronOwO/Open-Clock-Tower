using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>候选事实库里一条事实的定义（R-0057-C）：编码、分组、可选取值与求值函数。</summary>
/// <remarks>
/// 一条事实 = 一个纯函数：给定账与参数，回答"这句话此刻是对是错"。求值返回 null 表示**判不了**
/// （账上维度没观测齐）——那条候选根本不进候选集合，说书人也塞不进来（不猜，D-0015）。
/// 新增一条事实只加一个定义，不动状态机、不动投影（票据「方案」第 1 节末）。
/// </remarks>
internal sealed record SavantFactDefinition
{
    /// <summary>稳定编码（进事件流，永不改写）。</summary>
    public required string Code { get; init; }

    /// <summary>分组名（说书人端按它分栏）。</summary>
    public required string Group { get; init; }

    /// <summary>是不是点名类高强度信息（平台只标出来，用不用由说书人裁量）。</summary>
    public bool HighIntensity { get; init; }

    /// <summary>
    /// 互斥组：同组不同取值**互为反面**（奇 / 偶、善良 / 邪恶一类）。组合校验据此拒绝
    /// "两条互为反面"的搭配（C4）；null = 各取值彼此独立（如"3 号是邪恶"与"5 号是邪恶"）。
    /// </summary>
    public string? OppositeGroup { get; init; }

    /// <summary>可选取值（无参数事实给空集合）；顺序 = 候选顺序。</summary>
    public Func<SavantFactWorld, IReadOnlyList<string>> Parameters { get; init; } = _ => [];

    /// <summary>
    /// 求值：<c>(世界, 参数) → 人话 + 真值</c>；返回 null = 判不了（不进候选，硬塞在提交时被拒）。
    /// 参数不认识（客户端塞了编码外的取值）同样返回 null。
    /// </summary>
    public required Func<SavantFactWorld, string?, SavantFactEvaluation?> Evaluate { get; init; }
}
