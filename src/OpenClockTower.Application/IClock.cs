namespace OpenClockTower.Application;

/// <summary>
/// 宿主时钟：墙上时间只允许出现在应用 / 宿主层（Kernel 零时间，D-0008）。
/// </summary>
public interface IClock
{
    /// <summary>当前 UTC 时刻。</summary>
    DateTimeOffset UtcNow { get; }
}
