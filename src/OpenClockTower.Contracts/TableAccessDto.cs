namespace OpenClockTower.Contracts;

/// <summary>
/// 一桌的**访问模式**（D-0037）：说书人切换后即时推给该桌的全部连接。
/// </summary>
/// <remarks>
/// <para>
/// 它是"这条事实变了"的推送，不是游戏状态的一部分：访问模式是会话信息（存在会话目录里，
/// 不进事件流），权威读取口是大厅列表（<see cref="LobbyTableDto.InviteOnly"/>）。
/// 推送的用处是**不刷新不重连就变**——说书人刚把桌切成邀请制，在场的人当场看到。
/// </para>
/// <para>
/// 只带桌标识与一个布尔：不给任何席位信息，也不透露谁在做这个决定（D-0012 §4.3）。
/// </para>
/// </remarks>
public sealed record TableAccessDto
{
    /// <summary>哪一桌。</summary>
    public required string GameId { get; init; }

    /// <summary>true = 邀请制（自助入座关闭，要凭邀请码）；false = 公开桌。</summary>
    public required bool InviteOnly { get; init; }
}
