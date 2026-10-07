namespace OpenClockTower.Application;

/// <summary>
/// 把一桌从**内存注册表**里摘掉（M5 / G-A6-5：空闲桌回收的第二步）。
/// </summary>
/// <remarks>
/// <para>
/// 单方法接口看着单薄，它挡的是一件具体的事：回收要有**两个**入口——宿主里的定时清扫 / 开桌自愈，
/// 与拿着单实例锁跑的维护命令。后者进程里根本没有注册表（服务没在跑，所以一桌也没装载），
/// 但判定与删除必须与前者**逐字相同**。把"摘表"这一小步抽出来，两个入口就能共用同一个清扫器，
/// 而不是把那段循环抄第二遍（抄一遍就等于埋了第二次分叉）。
/// </para>
/// <para>
/// 放在 Application 而不是 Server：实现者 <see cref="GameRegistry"/> 在应用层，
/// 接口若留在宿主层就变成"下层实现上层的接口"。
/// </para>
/// </remarks>
public interface ITableUnloader
{
    /// <summary>摘掉一桌；返回它原本在不在册。没有注册表的进程（维护命令）恒返回 false。</summary>
    bool Unload(GameId gameId);
}
