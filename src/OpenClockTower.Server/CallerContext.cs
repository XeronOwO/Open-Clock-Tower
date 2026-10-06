namespace OpenClockTower.Server;

/// <summary>
/// **谁的请求**：发起这次调用的客户端地址与连接标识（M4 / G-A5-10 的审计口径）。
/// </summary>
/// <remarks>
/// <para>
/// 审计日志要能回答"是谁、从哪来"，而这两件事都在传输层：地址经 <see cref="ClientAddress"/>
/// 从可信代理头归一（M3 / G-A3-3），连接标识是 SignalR 给的。把它们做成一个值往后传，
/// 比让每个服务各自去摸 <c>HttpContext</c> 干净——服务层不需要认识 HTTP。
/// </para>
/// <para>
/// 只装审计用的两个字符串，**不装凭据**：本项目的口径是凭据明文永不进日志（D-0012），
/// 这里连字段都不给它留位置。
/// </para>
/// </remarks>
/// <param name="Client">客户端地址；取不到时为 <see cref="ClientAddress.Unknown"/>。</param>
/// <param name="ConnectionId">SignalR 连接标识。</param>
public readonly record struct CallerContext(string Client, string ConnectionId)
{
    /// <summary>从本次调用的 HTTP 上下文取出审计标识。</summary>
    public static CallerContext Of(HttpContext? httpContext, string connectionId) =>
        new(ClientAddress.Of(httpContext), connectionId);
}
