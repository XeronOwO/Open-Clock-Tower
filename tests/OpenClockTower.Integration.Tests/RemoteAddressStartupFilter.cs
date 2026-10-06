using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 测试专用：在管线最前面把"连接对端地址"摆成我们要的样子。
/// </summary>
/// <remarks>
/// <para>
/// 真宿主里这个值来自 socket（反代后面由 <c>UseForwardedHeaders</c> 按可信名单改写），
/// 而 TestServer 的连接是内存里的、<c>RemoteIpAddress</c> 默认是 <c>null</c>——
/// 于是"只信可信代理传来的转发头"与"按 IP 限速"这两条规则在测试里根本无从触发。
/// 这个过滤器**只为可测性存在**：它把输入摆好，判定仍然由产品代码里的
/// <c>ForwardedHeaderPolicy</c> 与 <c>AccountAttemptLimiter</c> 做，测试里没有第二份规则。
/// </para>
/// <para>
/// 取值顺序：请求头 <c>X-Test-Client-Address</c>（同一个宿主要模拟多个客户端时用）→
/// 配置 <c>Test:RemoteIpAddress</c>（整个宿主一个地址）→ 都不给就不动（保持 <c>null</c>）。
/// </para>
/// <para>
/// 注册方式（每个用得上它的宿主都要显式注册，**不放进产品管线**）：
/// <c>builder.ConfigureTestServices(services =&gt; services.AddSingleton&lt;IStartupFilter&gt;(...))</c>。
/// </para>
/// </remarks>
internal sealed class RemoteAddressStartupFilter(IConfiguration configuration) : IStartupFilter
{
    /// <summary>模拟客户端地址用的请求头名。</summary>
    internal const string HeaderName = "X-Test-Client-Address";

    /// <summary>把过滤器挂到管线最前面。</summary>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, following) =>
        {
            var raw = context.Request.Headers.TryGetValue(HeaderName, out var header) && header.Count > 0
                ? header[0]
                : configuration["Test:RemoteIpAddress"];

            if (!string.IsNullOrEmpty(raw) && IPAddress.TryParse(raw, out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return following();
        });

        next(app);
    };
}
