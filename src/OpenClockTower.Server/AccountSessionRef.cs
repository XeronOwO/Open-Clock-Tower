using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 一条账号会话的**服务端引用**（M2 / G-A2-1）：进程内标识 + 它属于哪个账号。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是这条不变式：**连接级凭据从属于授权它的账号会话**。会话被撤（登出 / 口令重置），
/// 由它签发的那些连接必须同时失去身份——否则"改口令 = 全场踢下线"只是一句空话。
/// 先把"是哪条会话"记在连接上，才谈得上"撤哪几条连接"。
/// </para>
/// <para>
/// 标识是本进程内的随机数，与会话凭据本身无关：不存凭据，也不存它的哈希副本。
/// 进程重启后会话本就全部失效，所以标识不跨进程、不落盘。
/// </para>
/// </remarks>
/// <param name="Id">本进程内的会话标识（同账号的多条登录各不相同）。</param>
/// <param name="Account">会话所属账号。</param>
public readonly record struct AccountSessionRef(Guid Id, AccountId Account)
{
    /// <summary>签发一条新的会话引用。</summary>
    public static AccountSessionRef CreateNew(AccountId account) => new(Guid.NewGuid(), account);

    /// <summary>日志用的短标识：够定位，不能反推。</summary>
    public override string ToString() => Id.ToString("N")[..8];
}
