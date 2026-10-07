namespace OpenClockTower.Application;

/// <summary>
/// 账号**注销**端口（M5 / G-A1-6）：把一个账号从库里抹掉，连带它占过的席位。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IAccountStore"/> 分开而不是往里加一个 <c>DeleteAsync</c>：注销不是"账号表少一行"，
/// 它是**跨表的一次动作**——账号行、它在每一桌的席位绑定、以及它在那些桌上的归属，
/// 必须一起成立或一起不成立。放进账号表端口会让"注销"看起来像一个单表更新。
/// </para>
/// <para>
/// 删除面**不含事件流**：见 <see cref="AccountErasureResult"/>。
/// </para>
/// </remarks>
public interface IAccountErasureStore
{
    /// <summary>
    /// 抹掉一个账号：删账号行 + 该账号在**所有桌**的席位绑定 + 把它开的桌的归属置空（一个事务）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 归属**置空而不是连带删桌**：那一桌上可能正有别人的对局，注销一个人不该毁掉一桌人的记录。
    /// 代价是那张桌从此没有主持台（D-0027 的归属认定只看开桌账号），所以它在语义上已经废弃——
    /// 交给空闲回收兜底（无人主持 ⇒ 不会再有新事件 ⇒ 到点回收），不需要为它单开一条删除路径。
    /// </para>
    /// <para>
    /// 账号标识**不回收**（<c>AUTOINCREMENT</c>）：老标识不会被后来者复用，
    /// 于是"这张桌原来的主人是谁"这种历史问题不会得到一个是非颠倒的答案。
    /// </para>
    /// <para>账号本来就不存在时返回 <c>Deleted = false</c>，不抛异常——注销要幂等。</para>
    /// </remarks>
    Task<AccountErasureResult> EraseAsync(AccountId accountId, CancellationToken cancellationToken);
}
