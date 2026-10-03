namespace OpenClockTower.Kernel;

/// <summary>说书人加了一条注记（D-0019）。</summary>
/// <remarks>
/// 注记不是游戏状态事实：它进事件流（可重放 / 可撤销 / 可审计），但只折进独立的注记账，
/// 不进 <see cref="GameState"/>、不进玩家投影。
/// </remarks>
public sealed record SeatAnnotationAddedEvent : GameEvent
{
    /// <summary>新增的注记（含签发标识、席位与归一化后的文本）。</summary>
    public required SeatAnnotation Annotation { get; init; }
}
