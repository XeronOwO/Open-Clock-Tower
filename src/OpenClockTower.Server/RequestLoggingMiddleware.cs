using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OpenClockTower.Server;

/// <summary>
/// 请求日志（M3 / G-A3-3）：每条请求一行，带**真实客户端地址**。
/// </summary>
/// <remarks>
/// <para>
/// 之所以要有这一条：反代后面 <c>Connection.RemoteIpAddress</c> 默认是反代自己的地址，
/// 于是"出事时是谁在打"这个问题在日志里根本无从回答。真实地址由
/// <see cref="ForwardedHeaderPolicy"/> 在管线最前面归一，本类只是把它写下来。
/// </para>
/// <para>
/// <b>只记路径、不记查询串</b>：SignalR 把连接令牌放在长连接请求的 <c>?id=</c> 上，
/// 查询串进日志等于把凭据写进磁盘。方法、路径、状态码、耗时、客户端地址足够定位问题。
/// </para>
/// <para>
/// 分级按触发频率：静态资源与 <c>/healthz</c>（监控每几秒一次）走 Debug，
/// 其余（页面、Hub 协商与升级）走 Information——默认配置下它们才是"人真的来了一次"的痕迹。
/// </para>
/// </remarks>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    /// <summary>记录请求并输出一行日志。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            logger.Log(
                LevelFor(context.Request.Path),
                "HTTP {Method} {Path} → {Status} 用时={ElapsedMilliseconds}ms 客户端={Client}",
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                context.Response.StatusCode,
                (long)elapsed.TotalMilliseconds,
                ClientAddress.Of(context));
        }
    }

    /// <summary>高频路径（静态产物、健康检查）降到 Debug，其余按 Information 记。</summary>
    private static LogLevel LevelFor(PathString path) =>
        path.StartsWithSegments("/healthz", StringComparison.Ordinal)
        || path.StartsWithSegments(ResponseHeadersMiddleware.HashedAssetPrefix, StringComparison.Ordinal)
            ? LogLevel.Debug
            : LogLevel.Information;
}
