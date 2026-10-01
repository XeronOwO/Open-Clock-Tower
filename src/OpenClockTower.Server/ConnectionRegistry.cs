using System.Collections.Concurrent;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 连接登记：席位 / 说书人 → 当前连接。
/// </summary>
/// <remarks>
/// 操作请求是**单播**（D-0013 §5）：只有当事玩家的连接收到推送，其他人的设备没有任何活动指示。
/// 重连时同一席位的新连接替换旧连接。
/// </remarks>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<SeatId, string> _seats = new();
    private readonly ConcurrentDictionary<string, SeatId> _connectionSeats = new();
    private readonly ConcurrentDictionary<string, string> _storytellers = new();

    /// <summary>当前已绑定的全部席位。</summary>
    public IReadOnlyCollection<SeatId> Seats => _seats.Keys.ToArray();

    /// <summary>当前已绑定的说书人连接。</summary>
    public IReadOnlyCollection<string> StorytellerConnections => _storytellers.Keys.ToArray();

    /// <summary>把席位绑定到连接（重连时覆盖旧连接）。</summary>
    public void BindSeat(SeatId seat, string connectionId)
    {
        _seats[seat] = connectionId;
        _connectionSeats[connectionId] = seat;
    }

    /// <summary>把连接登记为说书人连接。</summary>
    public void BindStoryteller(string connectionId) => _storytellers[connectionId] = connectionId;

    /// <summary>该连接是不是说书人连接。</summary>
    public bool IsStoryteller(string connectionId) => _storytellers.ContainsKey(connectionId);

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

    /// <summary>找出该连接绑定的席位；没有绑定则返回 null。</summary>
    public SeatId? FindSeat(string connectionId) =>
        _connectionSeats.TryGetValue(connectionId, out var seat) ? seat : null;

    /// <summary>连接断开时移除登记；只清理该连接自己的绑定，不误伤重连后的新连接。</summary>
    public void Remove(string connectionId)
    {
        if (_connectionSeats.TryRemove(connectionId, out var seat)
            && _seats.TryGetValue(seat, out var current)
            && string.Equals(current, connectionId, StringComparison.Ordinal))
        {
            _seats.TryRemove(seat, out _);
        }

        _storytellers.TryRemove(connectionId, out _);
    }
}
