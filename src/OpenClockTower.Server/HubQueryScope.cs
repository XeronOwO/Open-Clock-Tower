using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>
/// **查询类入口**：开局配板建议与复盘页——只读、不落账、不打命令的四道闸。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁，与 <see cref="HubJoinScope"/> / <see cref="HubTableAdmin"/>
/// 同一条理由）：Hub 只该做"翻译"，而这两条查询各自带一段真逻辑——一条要跑配板分布表、
/// 一条要把"复盘还不能看"翻成中性文案。身份闸仍留在 Hub（说书人凭据），**可见性闸在 Application**
/// （<see cref="ReplayQueryService"/>）：前端不判规则，服务端说了算。
/// </para>
/// <para>单例：只持有无状态协作者；"在哪一桌""谁在问"都属于每次调用，按参数传入。</para>
/// </remarks>
public sealed class HubQueryScope
{
    private readonly ILogger<HubQueryScope> _logger;

    /// <summary>构造查询入口。</summary>
    public HubQueryScope(ILogger<HubQueryScope> logger) => _logger = logger;

    /// <summary>
    /// 配板建议（只读、不落账）：按官方分布表 + 在场角色的设置调整生成建议。
    /// </summary>
    /// <param name="game">哪一桌。</param>
    /// <param name="seed">随机种子：可由客户端传入、缺省由服务端生成并回传（R-0041 / R-0042）。</param>
    /// <param name="nonTravellerCount">
    /// 配板覆盖的非旅行者人数（R-0046：旅行者是叠加角色，不占镇民 / 外来者 / 爪牙 / 恶魔名额）；
    /// null = 本局全部席位都是非旅行者（缺省语义）。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<SetupProposalDto> ProposeSetupAsync(
        GameInstance game,
        string? seed,
        int? nonTravellerCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var result = await game.Session.ProposeSetupAsync(seed, nonTravellerCount, cancellationToken);
        return ProjectionMapper.ToDto(result);
    }

    /// <summary>
    /// 查询一页复盘（D-0020 / R-0043）：说书人随时可看，玩家只有本局结束之后才允许。
    /// </summary>
    /// <param name="actor">提问者（凭据推导；D-0012）。</param>
    /// <param name="game">哪一桌。</param>
    /// <param name="afterSequence">客户端已拿到的最大事件序号；首次传 0。</param>
    /// <param name="pageSize">本页最多返回的步骤数（Application 侧钳制）。</param>
    /// <param name="connectionId">连接标识（拒绝时的审计要能回答"是谁在什么时候问的"）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<ReplayViewDto> GetReplayAsync(
        Actor actor,
        GameInstance game,
        long afterSequence,
        int pageSize,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        try
        {
            return ProjectionMapper.ToDto(
                await game.Replay.ReadAsync(actor, afterSequence, pageSize, cancellationToken));
        }
        catch (ReplayAccessDeniedException exception)
        {
            // 中性文案：只说明什么时候可以看，不泄露任何局面信息（R-0043）。
            _logger.LogInformation(
                "复盘查询被拒：connection={ConnectionId} kind={Kind} 原因={Reason}",
                connectionId,
                actor.Kind,
                exception.Message);
            throw new HubException(exception.Message);
        }
    }
}
