using System.Security.Cryptography;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>已签发凭据的服务端记录：只存哈希与它绑定的身份（D-0012：明文不落地）。</summary>
internal sealed class ConnectionCredentialRecord
{
    private readonly byte[] _hash;

    private ConnectionCredentialRecord(byte[] hash, ActorKind kind, SeatId? seat)
    {
        _hash = hash;
        Kind = kind;
        Seat = seat;
    }

    /// <summary>凭据绑定的身份类别：Player / Storyteller。</summary>
    public ActorKind Kind { get; }

    /// <summary>玩家身份对应的席位；说书人为 null。</summary>
    public SeatId? Seat { get; }

    /// <summary>为玩家席位签发一条记录。</summary>
    public static ConnectionCredentialRecord ForSeat(ConnectionCredential credential, SeatId seat) =>
        new(ConnectionCredential.HashOf(credential.Value), ActorKind.Player, seat);

    /// <summary>为说书人连接签发一条记录。</summary>
    public static ConnectionCredentialRecord ForStoryteller(ConnectionCredential credential) =>
        new(ConnectionCredential.HashOf(credential.Value), ActorKind.Storyteller, null);

    /// <summary>固定时间比较：只比哈希，不暴露"比到第几位"的时序。</summary>
    public bool Matches(ConnectionCredential credential) =>
        CryptographicOperations.FixedTimeEquals(_hash, ConnectionCredential.HashOf(credential.Value));
}
