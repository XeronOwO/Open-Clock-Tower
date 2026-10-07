using System.Collections.Concurrent;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 连接会话目录：**（桌, 席位）/ （桌, 说书人）** → 当前连接，以及每条连接签发的连接级凭据（D-0012 §4.1）。
/// </summary>
/// <remarks>
/// <para>
/// 单一所有者：连接绑定、凭据签发与作废都在这里，并且**所有变更都在同一把锁里完成**——
/// 签发是"读旧绑定 → 作废 → 写新绑定"的复合操作，拆成几个原子步骤就会被并发 join 交错出
/// 两条都有效的凭据（复核发现，2026-10-02）。
/// </para>
/// <para>
/// **多桌（D-0024）**：席位与说书人都按桌分区。这不是可选的细节——甲桌的 1 号与乙桌的 1 号
/// 是两张不同的席位，用全局 <see cref="SeatId"/> 作键会让两桌的路由互相覆盖：
/// 甲桌的裁定会推到乙桌坐在同一席位号的人手上。连接的所属桌在签发凭据时确定，
/// 之后 <see cref="Validate"/> 一并返回它，于是"这条连接在哪一桌"只有一处事实。
/// </para>
/// <para>
/// 重连语义（每桌各自成立）：同一席位的新连接替换旧连接、说书人同一时刻只允许一条有效连接，
/// **旧连接的凭据立即作废**。凭据身份与推送路由必须一致：<see cref="Validate"/> 会核对
/// "该桌该席位当前确实指向这条连接"，防止陈旧映射变成"零凭据也能收私有推送"的路由后门。
/// </para>
/// <para>
/// **撤销覆盖面（M2 / G-A2-1）**：每条连接还记着"是哪条账号会话授权了它"，于是登出 / 口令重置能一路
/// 打到**已经进门**的连接上（<see cref="RevokeSession"/> / <see cref="RevokeAccount"/>）——
/// 撤销不再只作用于"下一次进门"。
/// </para>
/// </remarks>
public sealed class ConnectionRegistry
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<(GameId Game, SeatId Seat), string> _seats = new();
    private readonly ConcurrentDictionary<string, ConnectionCredentialRecord> _credentials = new();
    private readonly ConcurrentDictionary<string, GameId> _connectionGames = new();

    /// <summary>某一桌当前已绑定的全部席位。</summary>
    public IReadOnlyCollection<SeatId> SeatsOf(GameId game) =>
        [.. _seats.Keys.Where(key => key.Game == game).Select(key => key.Seat).OrderBy(seat => seat.Value)];

    /// <summary>某一桌当前已绑定的说书人连接。</summary>
    public IReadOnlyCollection<string> StorytellerConnectionsOf(GameId game) =>
        [.. _credentials
            .Where(pair => pair.Value.Kind == ActorKind.Storyteller
                && _connectionGames.TryGetValue(pair.Key, out var gameId)
                && gameId == game)
            .Select(pair => pair.Key)];

    /// <summary>
    /// 这一桌当前有没有连接的痕迹（席位或主持台）——空闲桌回收据此判"正在被使用"（M5 / G-A6-5）。
    /// </summary>
    /// <remarks>
    /// 看的是**所有签名过的连接**（<c>_connectionGames</c>），不是只有已路由的席位：
    /// 一条刚建立、刚进主持台的连接与一条已入座的连接一样，都意味着"这一桌有人"。
    /// 连接上限是 512（M3 / G-A5-7），所以这里的一次线性扫描是常数级的小事
    /// ——为它单立一份"每桌连接计数"等于再养一份会和事实分叉的账。
    /// </remarks>
    public bool IsOccupied(GameId game) => _connectionGames.Values.Contains(game);

    /// <summary>玩家加入 / 重连：为这条连接签发凭据；同桌同席旧连接与同连接旧凭据立即作废。</summary>
    /// <param name="session">授权这次入座的账号会话（M2 / G-A2-1）；只凭票据的游客为 null。</param>
    public ConnectionCredential IssueForSeat(GameId game, SeatId seat, string connectionId, AccountSessionRef? session)
    {
        lock (_gate)
        {
            if (_seats.TryGetValue((game, seat), out var previous)
                && !string.Equals(previous, connectionId, StringComparison.Ordinal))
            {
                Revoke(previous);
            }

            Revoke(connectionId);

            var credential = ConnectionCredential.CreateNew();
            _seats[(game, seat)] = connectionId;
            _connectionGames[connectionId] = game;
            _credentials[connectionId] = ConnectionCredentialRecord.ForSeat(credential, seat, session);
            return credential;
        }
    }

    /// <summary>说书人加入 / 重连：为这条连接签发凭据；**本桌**其余说书人连接立即作废。</summary>
    /// <remarks>作废范围严格限定在本桌：甲桌换说书人不能把乙桌的说书人踢下线。</remarks>
    /// <param name="session">授权这次加入的账号会话（M2 / G-A2-1：主持权也在撤销面内）。</param>
    public ConnectionCredential IssueForStoryteller(GameId game, string connectionId, AccountSessionRef? session)
    {
        lock (_gate)
        {
            foreach (var existing in StorytellerConnectionsOf(game))
            {
                Revoke(existing);
            }

            Revoke(connectionId);

            var credential = ConnectionCredential.CreateNew();
            _connectionGames[connectionId] = game;
            _credentials[connectionId] = ConnectionCredentialRecord.ForStoryteller(credential, session);
            return credential;
        }
    }

    /// <summary>
    /// 校验凭据属于**当前这条连接**、且身份与席位路由一致；通过时一并给出这条连接在哪一桌。
    /// </summary>
    public CredentialValidation Validate(ConnectionCredential credential, string connectionId)
    {
        lock (_gate)
        {
            if (!_credentials.TryGetValue(connectionId, out var record)
                || !_connectionGames.TryGetValue(connectionId, out var game))
            {
                return CredentialValidation.Reject("这条连接没有有效凭据：请先用票据加入");
            }

            if (!record.Matches(credential))
            {
                return CredentialValidation.Reject("凭据与这条连接不匹配：可能来自旧连接（重连必须重新出示票据）");
            }

            if (record.Kind == ActorKind.Player
                && (record.Seat is not { } seat
                    || !_seats.TryGetValue((game, seat), out var routed)
                    || !string.Equals(routed, connectionId, StringComparison.Ordinal)))
            {
                return CredentialValidation.Reject("凭据身份与当前席位绑定不一致：请重新用票据加入");
            }

            return CredentialValidation.Accept(record.Kind, record.Seat, game);
        }
    }

    /// <summary>取**某一桌**某个席位的当前连接。</summary>
    public bool TryGetSeatConnection(GameId game, SeatId seat, out string connectionId)
    {
        if (_seats.TryGetValue((game, seat), out var found))
        {
            connectionId = found;
            return true;
        }

        connectionId = string.Empty;
        return false;
    }

    /// <summary>连接断开时移除登记与凭据；只清理指向这条连接自己的绑定，不误伤重连后的新连接。</summary>
    public void Remove(string connectionId)
    {
        lock (_gate)
        {
            Revoke(connectionId);
        }
    }

    /// <summary>
    /// 撤销一条账号会话授权的全部连接（登出 / 该会话失效，M2 / G-A2-1）；返回被撤的连接 id。
    /// </summary>
    /// <remarks>
    /// 撤的是**身份痕迹**（凭据 + 席位 / 说书人绑定），所以命令与推送一起停：只删凭据会留下
    /// "零凭据也能收私有推送"的路由后门（见 <see cref="Validate"/>）。只按会话精确匹配——
    /// 同账号在别的设备上的登录不受牵连。
    /// </remarks>
    public IReadOnlyList<string> RevokeSession(AccountSessionRef session) =>
        RevokeWhere(record => record.Session == session);

    /// <summary>撤销某个账号授权的全部连接（口令重置等账号级失效，M2 / G-A2-1）；返回被撤的连接 id。</summary>
    public IReadOnlyList<string> RevokeAccount(AccountId account) =>
        RevokeWhere(record => record.Session?.Account == account);

    /// <summary>按记录匹配撤连接：先收集再撤（枚举期间不改字典），全程持同一把锁。</summary>
    private IReadOnlyList<string> RevokeWhere(Func<ConnectionCredentialRecord, bool> match)
    {
        lock (_gate)
        {
            var revoked = _credentials
                .Where(pair => match(pair.Value))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var connectionId in revoked)
            {
                Revoke(connectionId);
            }

            return revoked;
        }
    }

    /// <summary>作废一条连接的全部身份痕迹（凭据 + 席位绑定 + 所属桌）。调用方必须持有 <c>_gate</c>。</summary>
    private void Revoke(string connectionId)
    {
        _credentials.TryRemove(connectionId, out _);
        _connectionGames.TryRemove(connectionId, out _);

        // 清理所有"指向这条连接"的前向席位映射：既有它自己的席位，也有"同一连接换席位"留下的陈旧项。
        foreach (var pair in _seats
                     .Where(pair => string.Equals(pair.Value, connectionId, StringComparison.Ordinal))
                     .ToArray())
        {
            _seats.TryRemove(pair.Key, out _);
        }
    }
}
