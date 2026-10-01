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
}
