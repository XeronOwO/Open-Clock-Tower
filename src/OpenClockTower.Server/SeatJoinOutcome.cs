using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>一次加入的结果（D-0012 / D-0021）：席位、连接凭据、重连包与"是否新建了认领"。</summary>
public sealed record SeatJoinOutcome
{
    /// <summary>定位到的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>本次加入携带的账号（**必需**：入座必须登录，D-0037；游客概念已随本批消失）。</summary>
    public required AccountId AccountId { get; init; }

    /// <summary>为这条连接签发的连接级凭据。</summary>
    public required ConnectionCredential Credential { get; init; }

    /// <summary>重连包：快照 + 从本地已知序号起的可见事件。</summary>
    public required ReconnectBundle Bundle { get; init; }

    /// <summary>本次是否**新建**了席位认领（true = 要通知全桌新名字）。</summary>
    public required bool Claimed { get; init; }
}
