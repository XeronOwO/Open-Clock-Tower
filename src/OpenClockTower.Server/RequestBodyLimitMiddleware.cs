using Microsoft.Extensions.Options;

namespace OpenClockTower.Server;

/// <summary>
/// 请求体上限的前置闸（M3 / G-A3-4）：<c>Content-Length</c> 一超限就回 413，**不等谁来读它**。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不是只设 Kestrel 的 <c>MaxRequestBodySize</c>：那个上限在**应用真的去读响应体**时才生效。
/// 而"不读体的端点"是存在的——SignalR 的 <c>/hub/*/negotiate</c> 就是：实测给协商端点发 2 MB
/// 也照样回 200（体被丢掉）。带宽白送、日志无痕，正是要拦的那种"没人管的入口"。
/// </para>
/// <para>
/// 两道闸各管一段，缺一不可：**这里**按声明长度提前拒（挡掉绝大多数），
/// Kestrel 那个按实际读取拒（挡掉分块传输这类没有 <c>Content-Length</c> 的），取值同一个配置。
/// </para>
/// <para>
/// 顺序：必须排在 <see cref="ResponseHeadersMiddleware"/> 之后——被拒的响应也要带安全头。
/// </para>
/// </remarks>
public sealed class RequestBodyLimitMiddleware(
    RequestDelegate next,
    IOptions<TransportLimitsOptions> limits,
    ILogger<RequestBodyLimitMiddleware> logger)
{
    private readonly TransportLimitsOptions _limits = limits.Value;

    /// <summary>按声明长度判定；超限即 413，不进入下游。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var declared = context.Request.ContentLength;
        if (declared is { } length && length > _limits.MaxRequestBodyBytes)
        {
            logger.LogWarning(
                "请求体超限被拒：path={Path} 声明长度={DeclaredBytes}B 上限={LimitBytes}B 客户端={Client}",
                context.Request.Path.Value ?? "/",
                length,
                _limits.MaxRequestBodyBytes,
                ClientAddress.Of(context));
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        await next(context);
    }
}
