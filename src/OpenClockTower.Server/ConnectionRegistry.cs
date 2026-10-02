using System.Collections.Concurrent;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 连接会话目录：席位 / 说书人 → 当前连接，以及每条连接签发的**连接级凭据**（D-0012 §4.1）。
/// </summary>
/// <remarks>
/// <para>
/// 单一所有者：连接绑定、凭据签发与作废都在这里，并且**所有变更都在同一把锁里完成**——
/// 签发是"读旧绑定 → 作废 → 写新绑定"的复合操作，拆成几个原子步骤就会被并发 join 交错出
/// 两条都有效的凭据（复核发现，2026-10-02）。
/// </para>
/// <para>
/// 重连语义：同一席位的新连接替换旧连接（说书人同一时刻只允许一条有效连接），
/// **旧连接的凭据立即作废**——即使旧连接的 TCP 还没断，它也不能再发任何命令。
/// 凭据身份与推送路由必须一致：<see cref="Validate"/> 会核对"该席位当前确实指向这条连接"，
/// 防止同一连接换席位留下的陈旧映射变成"零凭据也能收私有推送"的路由后门。
/// </para>
/// </remarks>
public sealed class ConnectionRegistry
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<SeatId, string> _seats = new();
    private readonly ConcurrentDictionary<string, SeatId> _connectionSeats = new();
    private readonly ConcurrentDictionary<string, string> _storytellers = new();
    private readonly ConcurrentDictionary<string, ConnectionCredentialRecord> _credentials = new();

    /// <summary>当前已绑定的全部席位。</summary>
    public IReadOnlyCollection<SeatId> Seats => _seats.Keys.ToArray();

    /// <summary>当前已绑定的说书人连接。</summary>
    public IReadOnlyCollection<string> StorytellerConnections => _storytellers.Keys.ToArray();

    /// <summary>玩家加入 / 重连：为这条连接签发凭据；同席旧连接与同连接旧凭据立即作废。</summary>
    public ConnectionCredential IssueForSeat(SeatId seat, string connectionId)
    {
        lock (_gate)
        {
            if (_seats.TryGetValue(seat, out var previous)
                && !string.Equals(previous, connectionId, StringComparison.Ordinal))
            {
                Revoke(previous);
            }

            Revoke(connectionId);

            var credential = ConnectionCredential.CreateNew();
            _seats[seat] = connectionId;
            _connectionSeats[connectionId] = seat;
            _credentials[connectionId] = ConnectionCredentialRecord.ForSeat(credential, seat);
            return credential;
        }
    }

    /// <summary>说书人加入 / 重连：为这条连接签发凭据；其余说书人连接立即作废。</summary>
    public ConnectionCredential IssueForStoryteller(string connectionId)
    {
        lock (_gate)
        {
            foreach (var existing in _storytellers.Keys.ToArray())
            {
                _storytellers.TryRemove(existing, out _);
                Revoke(existing);
            }

            Revoke(connectionId);

            var credential = ConnectionCredential.CreateNew();
            _storytellers[connectionId] = connectionId;
            _credentials[connectionId] = ConnectionCredentialRecord.ForStoryteller(credential);
            return credential;
        }
    }

    /// <summary>
    /// 校验凭据属于**当前这条连接**、且身份与席位路由一致：不通过的原因（缺凭据 / 不匹配 / 路由分叉）
    /// 供审计使用。
    /// </summary>
    public CredentialValidation Validate(ConnectionCredential credential, string connectionId)
    {
        lock (_gate)
        {
            if (!_credentials.TryGetValue(connectionId, out var record))
            {
                return CredentialValidation.Reject("这条连接没有有效凭据：请先用票据加入");
            }

            if (!record.Matches(credential))
            {
                return CredentialValidation.Reject("凭据与这条连接不匹配：可能来自旧连接（重连必须重新出示票据）");
            }

            if (record.Kind == ActorKind.Player
                && (record.Seat is not { } seat
                    || !_seats.TryGetValue(seat, out var routed)
                    || !string.Equals(routed, connectionId, StringComparison.Ordinal)))
            {
                return CredentialValidation.Reject("凭据身份与当前席位绑定不一致：请重新用票据加入");
            }

            return CredentialValidation.Accept(record.Kind, record.Seat);
        }
    }

    /// <summary>取席位的当前连接。</summary>
    public bool TryGetSeatConnection(SeatId seat, out string connectionId)
    {
        if (_seats.TryGetValue(seat, out var found))
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

    /// <summary>作废一条连接的全部身份痕迹（凭据 + 席位绑定 + 说书人绑定）。调用方必须持有 <c>_gate</c>。</summary>
    private void Revoke(string connectionId)
    {
        _credentials.TryRemove(connectionId, out _);
        _connectionSeats.TryRemove(connectionId, out _);

        // 清理所有"指向这条连接"的前向席位映射：既有它自己的席位，也有"同一连接换席位"留下的陈旧项。
        foreach (var pair in _seats
                     .Where(pair => string.Equals(pair.Value, connectionId, StringComparison.Ordinal))
                     .ToArray())
        {
            _seats.TryRemove(pair.Key, out _);
        }

        _storytellers.TryRemove(connectionId, out _);
    }
}
