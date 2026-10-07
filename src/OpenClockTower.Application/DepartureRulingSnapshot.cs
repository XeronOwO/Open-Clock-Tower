using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 最近一次离场裁定的快照（D-0037）：说书人批了还是驳了、附了什么说明。
/// </summary>
/// <remarks>
/// 与 <see cref="VoidedRequestSnapshot"/> 同族：由事件流派生（<see cref="SessionTrackers"/>），
/// 重启恢复时重算，不落独立存储。它的用途是让提出申请的旅行者**在刷新之后仍然看得到结果**——
/// 只靠一次推送的话，"被驳回"这件事会在刷新时消失（那是事件，不是状态）。
/// 新的申请一旦提出就把它清掉：同一席位只留最新一次结论，不堆历史（历史在复盘里）。
/// </remarks>
public sealed record DepartureRulingSnapshot
{
    /// <summary>被裁定的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>true = 批准（该席位随即离场）；false = 驳回。</summary>
    public required bool Approved { get; init; }

    /// <summary>说书人给出的说明（可空）。</summary>
    public string? Note { get; init; }

    /// <summary>裁定事件在事件流里的序号。</summary>
    public required long Sequence { get; init; }
}
