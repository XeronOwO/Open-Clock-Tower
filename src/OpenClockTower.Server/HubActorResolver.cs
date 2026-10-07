using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 凭据 → 身份的唯一入口（D-0012 §4.1）：连接级凭据校验、玩家 / 说书人身份推导与拒绝审计。
/// </summary>
/// <remarks>
/// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁）：Hub 只保留"连接 / 调用 / 推送"，
/// "这条连接是谁"归这里。零信任口径不变：凭据只在签发它的连接上有效，明文永不进日志、只记短指纹；
/// 通过后由四道闸判"这个身份能不能发这条命令"。
/// </remarks>
public sealed class HubActorResolver
{
    private readonly ConnectionRegistry _registry;
    private readonly ILogger _logger;

    /// <summary>构造解析器（与 <see cref="GameHub"/> 同生命周期：单例）。</summary>
    public HubActorResolver(ConnectionRegistry registry, ILogger<HubActorResolver> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>解析玩家 / 说书人身份；凭据无效直接拒绝，且**不触达 Application**。</summary>
    /// <param name="credential">连接级凭据（客户端每条命令都要出示）。</param>
    /// <param name="connectionId">当前 connection，凭据只在这条连接上有效。</param>
    /// <param name="method">Hub 方法名（审计用）。</param>
    internal Actor Resolve(string? credential, string connectionId, string method)
    {
        var validation = Validate(credential, connectionId, method);
        if (!validation.Accepted)
        {
            throw new HubException("连接凭据无效：请先加入这一桌（D-0012）");
        }

        return validation.Kind == ActorKind.Player && validation.Seat is { } seat
            ? Actor.Player(seat)
            : Actor.Storyteller();
    }

    /// <summary>查询类入口要求说书人身份（查询不属于命令，不走四道闸）。</summary>
    internal Actor ResolveStoryteller(string? credential, string connectionId, string method)
    {
        var actor = Resolve(credential, connectionId, method);
        if (actor.Kind != ActorKind.Storyteller)
        {
            _logger.LogWarning(
                "查询被拒（身份）：connection={ConnectionId} 方法={Method} 原因=玩家连接不能读说书人视图",
                connectionId,
                method);
            throw new HubException("当前连接不是有效的说书人连接（D-0012）");
        }

        return actor;
    }

    /// <summary>校验凭据并审计失败（谁、哪条连接、什么方法、凭据短指纹、原因）——绝不写凭据明文。</summary>
    private CredentialValidation Validate(string? credential, string connectionId, string method)
    {
        var presented = new ConnectionCredential(credential ?? string.Empty);
        var validation = _registry.Validate(presented, connectionId);
        if (!validation.Accepted)
        {
            _logger.LogWarning(
                "命令被拒绝（凭据闸）：connection={ConnectionId} 方法={Method} 指纹={Fingerprint} 原因={Reason}",
                connectionId,
                method,
                ConnectionCredential.FingerprintOf(credential),
                validation.Reason);
        }

        return validation;
    }
}
