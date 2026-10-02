namespace OpenClockTower.Contracts;

/// <summary>
/// 呆瓜的公开选择记录（含"没有选择"的跳过）。
/// </summary>
/// <remarks>
/// 呆瓜的选择与说书人作废导致的跳过都是**公开事实**（R-0027）：玩家端与说书人端看到同一份记录。
/// </remarks>
public sealed record KlutzChoiceDto
{
    /// <summary>
    /// 这份记录被表达时的序号：推送 = 背书事件序号；快照 = 快照序号。
    /// 客户端按它做字段级取舍（同请求 / 白天投影的既有口径）。
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>呆瓜席位。</summary>
    public required int Seat { get; init; }

    /// <summary>被选中的席位；没有做出选择时为 null（跳过）。</summary>
    public int? Target { get; init; }

    /// <summary>是否真的做出了选择。</summary>
    public required bool Made { get; init; }

    /// <summary>说明（选择结果或跳过原因）。</summary>
    public required string Detail { get; init; }
}
