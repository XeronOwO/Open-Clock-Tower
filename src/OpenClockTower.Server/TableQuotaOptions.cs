namespace OpenClockTower.Server;

/// <summary>
/// 在册桌数的配额（M4 / G-A5-5）：**开桌是资源消耗**，不是无成本动作。
/// </summary>
/// <remarks>
/// <para>
/// 审计的读数很直接：`TableCreationPolicy` 只问"登录了没"，于是**一次匿名注册就能反复开桌**——
/// 每张桌都要装载事件流、起定时器、参与 200 ms 一轮的节拍扫描（<c>StepPacerHostedService</c>），
/// 而"没人玩的桌"从前**不回收**。这是把服务打垮的最短路径。
/// </para>
/// <para>
/// 两个额度各管一件事：**单账号额度**拦"一个人刷"，**全局额度**拦"很多人一起刷"。
/// 全局额度曾经是**硬顶**（没人玩的桌不回收 ⇒ 到顶后谁都开不出新桌）；
/// M5 第三刀之后它不再是硬顶：判定之前宿主会先回收一轮空闲桌（<see cref="TableRetentionOptions"/>），
/// 清理路径也从"运维手工五表联删"变成 `retire-tables`（部署文档 §9.4）。
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
    /// 取值按"说书人自己开局"给：一局一张桌，12 张足够一个人玩很久；
    /// 而刷桌的人被卡在这里，成本从"无限"变成"12 张"——他占的空桌到保留期（默认 24 小时）会被回收。
    /// </remarks>
    public int MaxTablesPerAccount { get; set; } = 12;

    /// <summary>
    /// **整个部署**在册的桌数上限，默认 64。
    /// </summary>
    /// <remarks>
    /// 到顶之后**先回收一轮空闲桌再判**（M5 第三刀）：这样它是"挡住无限堆积"的闸，
    /// 而不是"连没人玩的空壳也算数"的死墙。真到顶（一张空闲桌都没有）时仍然是拒绝，
    /// 拒绝文案说清怎么办。取值给得宽：它是部署容量的表达。
    /// </remarks>
    public int MaxTablesGlobal { get; set; } = 64;
}
