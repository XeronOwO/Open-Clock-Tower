namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人强推当前槽位：越过剩余配额与挂起，直接进入下一槽位（D-0014 兜底）。
/// </summary>
/// <remarks>
/// 这是"自动化只是建议"的直接体现：玩家卡住、逻辑出错、阻塞报警时，兜底入口永远可用。
/// 与 D-0013 的边界：它**允许**改变节奏——这是蓄意的人工例外，进事件流、可审计。
/// </remarks>
public sealed record ForceAdvanceInput : StepMachineInput
{
    /// <summary>强推原因（必须给出，落事件流）。</summary>
    public required string Reason { get; init; }
}
