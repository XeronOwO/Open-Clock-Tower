namespace OpenClockTower.Kernel;

/// <summary>
/// 博学者白天向说书人要两条信息：请求进事件流（只说书人可见——两条信息的内容只到本人）。
/// </summary>
/// <remarks>
/// 依据：百科《博学者》· 2026-10-01 抓取 · 角色能力（「每个白天，你可以**私下**询问说书人」）。
/// </remarks>
public sealed record SavantQuestionAskedEvent : GameEvent
{
    /// <summary>提问的博学者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>提问时刻该席位的角色（结清时的来源检索键）。</summary>
    public required CharacterId Character { get; init; }
}
