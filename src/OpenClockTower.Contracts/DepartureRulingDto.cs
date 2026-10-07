namespace OpenClockTower.Contracts;

/// <summary>
/// 最近一次离场裁定的结论（D-0037）：只下发给**申请人本人**。
/// </summary>
/// <remarks>
/// 它是"事件"而不是"状态"，但申请人刷新之后仍该看得到结果，所以随本人视图一起下发
/// （快照 + 推送同一份事实，见 D-0010 的同步口径）。
/// </remarks>
public sealed record DepartureRulingDto
{
    /// <summary>被裁定的席位（= 收件人本人）。</summary>
    public required int Seat { get; init; }

    /// <summary>true = 批准（该席位随即离场）；false = 驳回。</summary>
    public required bool Approved { get; init; }

    /// <summary>说书人给出的说明；可为 null。</summary>
    public string? Note { get; init; }

    /// <summary>裁定事件在事件流里的序号。</summary>
    public required long Sequence { get; init; }
}
