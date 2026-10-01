namespace OpenClockTower.Kernel;

/// <summary>
/// 阻塞报警：无合法选项且声明为阻塞（R-0009），等说书人处理。
/// </summary>
/// <remarks>
/// 阻塞不是死锁：D-0014 要求兜底入口永远开着——说书人可强推当前槽位、接管或重建。
/// </remarks>
public sealed record StepBlock
{
    /// <summary>阻塞原因（人类可读，给说书人定位）。</summary>
    public required string Reason { get; init; }
}
