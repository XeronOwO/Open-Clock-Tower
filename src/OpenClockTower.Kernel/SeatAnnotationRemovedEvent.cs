namespace OpenClockTower.Kernel;

/// <summary>说书人删了一条注记（D-0019）。</summary>
/// <remarks>
/// 事件携带被删的注记本身（含席位与文本），审计能回看"删掉的是什么"；
/// 折叠层只把它从注记账里移除，**不物理删除历史**（历史在事件流里）。
/// </remarks>
public sealed record SeatAnnotationRemovedEvent : GameEvent
{
    /// <summary>被删除的注记。</summary>
    public required SeatAnnotation Annotation { get; init; }
}
