namespace OpenClockTower.Kernel;

/// <summary>
/// 处罚处决输入：说书人绕过提名流程主动处决某席位（洗脑师 / 畸形秀演员的"疯狂"后果）。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0020：白天形态占用当天处决上限并立即收口白天；
/// 夜晚形态不占任何白天上限、不推进阶段。依据是否成立由规则层契约给出
/// （<see cref="IAdjudicatedExecutionSource"/>），内核只做账与阶段收口。
/// </remarks>
public sealed record PunishExecutionInput : StepMachineInput
{
    /// <summary>被处罚处决的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>处罚来源（决定依据契约与死亡归因）。</summary>
    public required MadnessPunishmentSource Source { get; init; }

    /// <summary>说书人的执行说明（进审计与处决事件；可空）。</summary>
    public string? Note { get; init; }
}
