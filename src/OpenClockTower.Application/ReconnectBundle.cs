namespace OpenClockTower.Application;

/// <summary>
/// 重连包：当前投影（快照）+ 从客户端已知序号起的**该玩家可见事件**（D-0010 / §5）。
/// </summary>
/// <remarks>
/// 断线重连 = 快照 + 补齐；补齐的事件必须按接收者投影（<see cref="PlayerEvent"/>），
/// 原始事件流里有他人请求与推进进度，绝不能整条下发（D-0012 §4.3、D-0013 §5）。
/// 未响应的操作请求由服务端在重连后**重新投递**（D-0011 硬约束 2）。
/// </remarks>
public sealed record ReconnectBundle
{
    /// <summary>快照对应的最新事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>该玩家的最新投影。</summary>
    public required PlayerView View { get; init; }

    /// <summary>序号大于客户端已知序号、且允许该玩家看到的事件。</summary>
    public required IReadOnlyList<PlayerEvent> EventsSince { get; init; }
}
