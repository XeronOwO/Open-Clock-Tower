namespace OpenClockTower.Application;

/// <summary>
/// 节奏配置：每个槽位的**最短**时间配额（D-0013 §2）。
/// </summary>
/// <remarks>
/// 默认 10 秒，落在百科《规则概要》二-2「等待五到十秒」的口径内——本决策把这条特例一般化
/// 到所有槽位，且配额不随"实际有几个角色行动"变化。验收测试用毫秒级配额的同一链路。
/// </remarks>
public sealed record PacingOptions
{
    /// <summary>每个槽位的最短时间配额。</summary>
    public required TimeSpan SlotQuota { get; init; }

    /// <summary>默认配置（10 秒 / 槽位）。</summary>
    public static PacingOptions Default { get; } = new() { SlotQuota = TimeSpan.FromSeconds(10) };
}
