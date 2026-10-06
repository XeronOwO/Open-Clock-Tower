namespace OpenClockTower.Server;

/// <summary>
/// 游戏内动作的频率口径（M4 / G-A5-6 · G-A5-8 的频率半）：入座与"写文本"这两类**有真实代价**的动作。
/// </summary>
/// <remarks>
/// <para>
/// 为什么是这两类，而不是"所有命令"：
/// **入座**每次都要把整条事件流从头读一遍重建重连包（<c>SessionQueries.ReconnectBundleAsync</c> 按
/// `afterSequence: 0` 读全量），对局越长单次越贵；**写文本**（原因 / 说明 / 注记）每次都要落事件行、
/// 写日志，是事件流与日志膨胀的唯一入口。其余命令要么有内核的合法性闸、要么本来就是高频且廉价。
/// </para>
/// <para>
/// 取值宽松是有意的：这条闸拦的是"循环调用"，不是"玩得快"。真人一局里的动作量级在几十次，
/// 额度按**一个数量级以上的余量**给（见各字段默认值），被拦下来的基本都是脚本。
/// </para>
/// <para>
/// 与账号入口限速同源：都是 <see cref="WindowedCounters"/> 上的窗口计数，都只在进程内存、
/// 重启归零、多实例各算一份（D-0033 代价）。
/// </para>
/// </remarks>
public sealed class ActionThrottleOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer:ActionThrottle";

    /// <summary>统计窗口（秒），默认 300（5 分钟）。</summary>
    public double WindowSeconds { get; set; } = 300;

    /// <summary>
    /// 同一（客户端 + 桌）的入座 / 重连次数上限，默认 60。
    /// </summary>
    /// <remarks>一局里连上带断线重连正常在 10 次上下；60 是"够用且拦得住循环"的量级。</remarks>
    public int JoinCallsPerClientAndGame { get; set; } = 60;

    /// <summary>
    /// 同一客户端的入座次数上限（跨桌），默认 120。
    /// </summary>
    /// <remarks>NAT 后面几个人同时开几桌时，单桌额度会各算各的，这一条是那条路的兜底。</remarks>
    public int JoinCallsPerClient { get; set; } = 120;

    /// <summary>
    /// 同一（客户端 + 桌 + 身份）写文本的次数上限，默认 120。
    /// </summary>
    /// <remarks>说书人一局里的注记 / 说明 / 原因合起来在几十次量级；120 留给"记得细"的主持人。</remarks>
    public int WriteTextCallsPerActor { get; set; } = 120;

    /// <summary>内存里最多跟踪多少个计数键，默认 20000（键比账号入口多一维：桌与身份）。</summary>
    public int MaxTrackedKeys { get; set; } = 20_000;
}
