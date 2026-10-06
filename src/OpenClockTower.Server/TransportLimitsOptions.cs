namespace OpenClockTower.Server;

/// <summary>
/// 传输面上限（M3 / G-A3-4）：请求体、SignalR 消息、连接数与超时**一律显式取值**。
/// </summary>
/// <remarks>
/// <para>
/// 显式写出来的理由不只是"设个上限"，而是**不吃框架默认值**：默认值随框架升级变化，
/// 而项目对此毫无感知——30 MB 的请求体、32 KB 的消息、无上限的连接数、30 秒的请求头超时都是这么来的。
/// </para>
/// <para>
/// 取值与依据登记在 <c>docs/decisions/active.md</c> D-0031；反代侧另有一层同类上限（nginx 模板），
/// 两层都要（应用不依赖反代存在，反代挡住应用之前的那一段）。
/// </para>
/// </remarks>
public sealed class TransportLimitsOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer:Transport";

    /// <summary>
    /// HTTP 请求体上限（字节），默认 256 KB。
    /// </summary>
    /// <remarks>
    /// 本宿主的 POST 只有 SignalR 的协商与长轮询（消息体另有 <see cref="MaxSignalRMessageBytes"/> 管），
    /// 没有文件上传，所以比框架默认的 30 MB 小两个数量级；长轮询一帧 = 消息 + JSON 包装，留了 4 倍余量。
    /// </remarks>
    public long MaxRequestBodyBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// SignalR 单帧消息上限（字节），默认 64 KB。
    /// </summary>
    /// <remarks>框架默认 32 KB。命令里最大的是注记 / 裁定理由这类自由文本（长度门禁在 M4 补）。</remarks>
    public long MaxSignalRMessageBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Kestrel 并发连接上限，默认 512。
    /// </summary>
    /// <remarks>
    /// 进程级兜底，不是按 IP 的配额——按 IP 的那一层在反代（nginx <c>limit_conn</c>）。
    /// 512 对"一桌 7 席 × 2 条连接 + 若干围观"的量级留了极宽余量，只拦"无限堆连接"。
    /// </remarks>
    public long MaxConcurrentConnections { get; set; } = 512;

    /// <summary>
    /// 读取请求头的超时（秒），默认 15。
    /// </summary>
    /// <remarks>框架默认 30 秒。这是慢连接（slowloris）最便宜的一道闸：只发一半请求头就占住连接的时代到此为止。</remarks>
    public double RequestHeadersTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// 空闲长连接的超时（秒），默认 60。
    /// </summary>
    /// <remarks>
    /// 框架默认 130 秒。注意**不作用于已升级的 WebSocket**（对局中的长连接由 SignalR 的心跳与
    /// nginx 的 <c>proxy_read_timeout</c> 两侧共同维持），只管 HTTP 空闲连接。
    /// </remarks>
    public double KeepAliveTimeoutSeconds { get; set; } = 60;
}
