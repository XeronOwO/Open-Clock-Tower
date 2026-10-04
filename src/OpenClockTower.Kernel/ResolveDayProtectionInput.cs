namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人裁定某席位「今天的死亡保护」（R-0048）：只在流放收票已收完、票面达线、目标存活且尚未裁定时受理。
/// </summary>
/// <remarks>
/// 走 <see cref="DayProtectionMachine"/> 校验；结论记进 <see cref="DayRecord.ProtectionDecisions"/>，
/// 由规则层的死亡保护来源读回（怪咖：裁定「今天是否有趣」对应受保护 / 不受保护）。
/// </remarks>
public sealed record ResolveDayProtectionInput : StepMachineInput
{
    /// <summary>被裁定的席位（必须是当前达线流放的目标）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>true = 今天受死亡保护；false = 不受保护。</summary>
    public required bool Protected { get; init; }

    /// <summary>说书人的说明（可选，进裁定事件）。</summary>
    public string? Note { get; init; }
}
