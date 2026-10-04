using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>一条命令的处理结果：状态变化只通过事件表达。</summary>
public sealed record CommandResult
{
    /// <summary>结果类别。</summary>
    public required CommandResultKind Kind { get; init; }

    /// <summary>提交后的事件流序号（Accepted / Duplicate 时有效）。</summary>
    public required long Sequence { get; init; }

    /// <summary>本次（或首次）产出的事件。</summary>
    public required IReadOnlyList<GameEvent> Events { get; init; }

    /// <summary>需要推送给客户端的通知（服务端在提交之后发出）。</summary>
    public required IReadOnlyList<GameNotification> Notifications { get; init; }

    /// <summary>被拒绝的原因；其它类别为 null。</summary>
    public CommandRejection? Rejection { get; init; }

    /// <summary>失败说明（内部异常 / 重建失败）；其它类别为 null。</summary>
    public string? Failure { get; init; }

    /// <summary>房间重建报告；仅重建命令非空。</summary>
    public RoomRebuildReport? Rebuild { get; init; }

    /// <summary>
    /// 本次为旅行者加入签发的席位；仅"加入旅行者且服务端分配席位"的命令非空（含重复投递回填）。
    /// </summary>
    public SeatId? IssuedSeat { get; init; }

    /// <summary>
    /// 本次签发的席位票据（说书人转交给新到场的玩家）；仅"加入旅行者且服务端分配席位"的命令非空。
    /// 票据是入场凭据：只回给出命令的说书人，不进事件流、不进投影。
    /// </summary>
    public string? IssuedSeatTicket { get; init; }
}
