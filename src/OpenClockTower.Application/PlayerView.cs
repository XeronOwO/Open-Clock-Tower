using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 发给某个玩家的投影：他只能看到自己该看到的东西。
/// </summary>
/// <remarks>
/// 依据 D-0013 §5 与 D-0012 §4.3：这里**没有**轮次、进度、槽位、他人活动等信息——
/// 玩家端在夜晚只有统一界面。信息隔离在服务端投影强制，不依赖前端不显示。
/// </remarks>
public sealed record PlayerView
{
    /// <summary>接收者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>当前大阶段（昼夜属公开信息）；未开局为 null。</summary>
    public GamePhase? Phase { get; init; }

    /// <summary>只包含发给该席位、且仍在等待响应的请求。</summary>
    public OperationRequest? PendingRequest { get; init; }

    /// <summary>投影对应的事件流序号（重连补齐用）。</summary>
    public required long Sequence { get; init; }
}
