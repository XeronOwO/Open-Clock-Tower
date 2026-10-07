using System.Security.Cryptography;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>已签发凭据的服务端记录：只存哈希与它绑定的身份（D-0012：明文不落地）。</summary>
/// <remarks>身份包含"是哪条账号会话授权了这条连接"（M2 / G-A2-1）——撤销要打得着，就得先记得住。</remarks>
internal sealed class ConnectionCredentialRecord
{
    private readonly byte[] _hash;

    private ConnectionCredentialRecord(byte[] hash, ActorKind kind, SeatId? seat, AccountSessionRef session)
    {
        _hash = hash;
        Kind = kind;
        Seat = seat;
        Session = session;
    }

    /// <summary>凭据绑定的身份类别：Player / Storyteller。</summary>
    public ActorKind Kind { get; }

    /// <summary>玩家身份对应的席位；说书人为 null。</summary>
    public SeatId? Seat { get; }

    /// <summary>
    /// 授权这条连接的账号会话（M2 / G-A2-1）：会话一被撤，这条连接的身份随之作废。
    /// **不可为空**（D-0037）：入座必须登录，于是每条连接凭据背后都有一条会话可撤——
    /// 此前"只凭票据入座的游客"为 null，那条路已整个删除。
    /// </summary>
    public AccountSessionRef Session { get; }

    /// <summary>为玩家席位签发一条记录。</summary>
    public static ConnectionCredentialRecord ForSeat(
        ConnectionCredential credential,
        SeatId seat,
        AccountSessionRef session) =>
        new(ConnectionCredential.HashOf(credential.Value), ActorKind.Player, seat, session);

    /// <summary>为说书人连接签发一条记录。</summary>
    public static ConnectionCredentialRecord ForStoryteller(
        ConnectionCredential credential,
        AccountSessionRef session) =>
        new(ConnectionCredential.HashOf(credential.Value), ActorKind.Storyteller, null, session);

    /// <summary>固定时间比较：只比哈希，不暴露"比到第几位"的时序。</summary>
    public bool Matches(ConnectionCredential credential) =>
        CryptographicOperations.FixedTimeEquals(_hash, ConnectionCredential.HashOf(credential.Value));
}
