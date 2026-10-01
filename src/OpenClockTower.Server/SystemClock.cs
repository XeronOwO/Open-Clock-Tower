using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>宿主时钟：唯一允许读墙上时间的地方。</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
