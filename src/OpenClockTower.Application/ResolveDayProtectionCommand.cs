using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人 / 宿主裁定某席位「今天的死亡保护」（R-0048）：只在流放收票已收完、票面达线且尚未裁定时受理。
/// </summary>
/// <remarks>
/// 结论记进当天账（<c>DayRecord.ProtectionDecisions</c>），由规则层死亡保护来源读回
/// （怪咖：说书人回答「今天是否有趣」——有趣 = 受保护）。受理条件与拒绝码由内核给出
/// （票据 `traveller-and-exile`「D3 实施口径」）。
/// </remarks>
public sealed record ResolveDayProtectionCommand : GameCommand
{
    /// <summary>被裁定的席位（必须是当前达线流放的目标）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>true = 今天受死亡保护；false = 不受保护。</summary>
    public required bool Protected { get; init; }

    /// <summary>说书人的说明（可选，进裁定事件）。</summary>
    public string? Note { get; init; }
}
