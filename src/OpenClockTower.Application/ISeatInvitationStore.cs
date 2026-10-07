using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 席位邀请凭据表（D-0038）：一席至多一条邀请，**只存哈希**。
/// </summary>
/// <remarks>
/// 与席位名单分开存是有意的（"状态属于所有者"）：席位名单由会话目录所有、随旅行者加入整行改写，
/// 而邀请凭据由说书人的签发动作单独写。挤在同一行里，一次"追加席位"的整行回写就会把别人刚签的
/// 邀请悄悄覆盖掉。
/// </remarks>
public interface ISeatInvitationStore
{
    /// <summary>
    /// 列出本局全部邀请（核验要逐条做固定时间比较，因此一次取全——一桌的席位数是个小数字）。
    /// </summary>
    Task<IReadOnlyList<SeatInvitation>> ListByGameAsync(GameId gameId, CancellationToken cancellationToken);

    /// <summary>
    /// 写入 / 覆盖某席位的邀请：**轮换 = 覆盖**，旧的那一枚当场失效（不需要"先删后建"两步）。
    /// </summary>
    Task ReplaceAsync(GameId gameId, SeatInvitation invitation, CancellationToken cancellationToken);
}
