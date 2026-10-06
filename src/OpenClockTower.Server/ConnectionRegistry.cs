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

    /// <summary>玩家加入 / 重连：为这条连接签发凭据；同桌同席旧连接与同连接旧凭据立即作废。</summary>
    public ConnectionCredential IssueForSeat(GameId game, SeatId seat, string connectionId)
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
            _credentials[connectionId] = ConnectionCredentialRecord.ForSeat(credential, seat);
            return credential;
        }
    }

    /// <summary>说书人加入 / 重连：为这条连接签发凭据；**本桌**其余说书人连接立即作废。</summary>
    /// <remarks>作废范围严格限定在本桌：甲桌换说书人不能把乙桌的说书人踢下线。</remarks>
    public ConnectionCredential IssueForStoryteller(GameId game, string connectionId)
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
            _credentials[connectionId] = ConnectionCredentialRecord.ForStoryteller(credential);
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
