namespace OpenClockTower.Kernel;

/// <summary>中断后的收票继续：重新起一次倒计时，从下一未收席位接着收（R-0017 目标形态）。</summary>
/// <remarks>
/// 服务端重启 / 重建时未完成的收票**不追补**——断线期间玩家无法举手，控制面恢复后由说书人
/// 显式继续；事件流里每席实际收集时刻仍以存储时间戳如实保留，不伪造历史。
/// </remarks>
public sealed record VoteSweepResumedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>第几次提名。</summary>
    public required int NominationIndex { get; init; }
}
