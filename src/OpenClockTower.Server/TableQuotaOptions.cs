namespace OpenClockTower.Server;

/// <summary>
/// 在册桌数的配额（M4 / G-A5-5）：**开桌是资源消耗**，不是无成本动作。
/// </summary>
/// <remarks>
/// <para>
/// 审计的读数很直接：`TableCreationPolicy` 只问"登录了没"，于是**一次匿名注册就能反复开桌**——
/// 每张桌都要装载事件流、起定时器、参与 200 ms 一轮的节拍扫描（<c>StepPacerHostedService</c>），
/// 而"没人玩的桌"首版**不回收**。这是把服务打垮的最短路径。
/// </para>
/// <para>
/// 两个额度各管一件事：**单账号额度**拦"一个人刷"，**全局额度**拦"很多人一起刷"。
/// 全局额度是硬顶——它在"没人玩的桌不回收"的前提下同时是**部署容量**的表达，所以要给得宽、
/// 并且让运维知道怎么清理（部署文档 §9.4）：项目首版没有关桌功能，清理 = 从目录里删行。
/// </para>
/// </remarks>
public sealed class TableQuotaOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer:TableQuota";

    /// <summary>
    /// 单个账号名下**在册**的桌数上限，默认 12。
    /// </summary>
    /// <remarks>
    /// 取值按"说书人自己开局"给：一局一张桌、首版不回收，12 张足够一个人玩很久；
    /// 而刷桌的人被卡在这里，成本从"无限"变成"12 张"。
    /// </remarks>
    public int MaxTablesPerAccount { get; set; } = 12;

    /// <summary>
    /// **整个部署**在册的桌数上限，默认 64。
    /// </summary>
    /// <remarks>
    /// 到顶之后谁都开不出新桌，必须由运维清理（部署文档 §9.4）。这是有意的硬顶：
    /// 与其让"没人玩的桌"无限堆积把进程拖垮，不如在一个可解释的数字上停住并说出来。
    /// 空闲桌回收排进 M5（G-A5-5 的容量半边）。
    /// </remarks>
    public int MaxTablesGlobal { get; set; } = 64;
}
