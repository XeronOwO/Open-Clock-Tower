using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace OpenClockTower.Server;

/// <summary>反代真实 IP 的口径（M3 / G-A3-3）：<c>X-Forwarded-*</c> **只从可信来源采信**。</summary>
/// <remarks>
/// <para>
/// 默认只信回环：本项目的部署形态是同机 nginx（<c>proxy_set_header X-Real-IP / X-Forwarded-For</c>），
/// "最近的一跳是本机"就是可信的全部条件。反代在别的机器或容器里时，由
/// <c>GameServer__TrustedProxies</c> 显式列出它的地址或网段（CIDR）——**不列就不信**。
/// </para>
/// <para>
/// 名单写错（拼错 IP、网段格式不对）直接让宿主起不来，不静默跳过：真实 IP 一失效，
/// 限速会退化成"所有请求同一个桶"，几十次失败之后全站都登不进来——那种故障在运行期极难定位，
/// 必须在启动时炸掉。
/// </para>
/// <para>
/// 只信最近的一跳（<c>ForwardLimit = 1</c>）：客户端的 <c>X-Forwarded-For</c> 可以是伪造的，
/// nginx 用 <c>$proxy_add_x_forwarded_for</c> 把真实地址**追加在最后**，所以只有最后一段算数，
/// 前面的一律丢弃。
/// </para>
/// </remarks>
public static class ForwardedHeaderPolicy
{
    /// <summary>把配置里的可信代理名单装进框架的转发头选项。</summary>
    /// <param name="options">框架选项（就地修改）。</param>
    /// <param name="trustedProxies">配置里的地址或网段清单（可空 = 只有回环）。</param>
    /// <exception cref="InvalidOperationException">清单里有一项不是合法 IP 或网段。</exception>
    public static void Apply(ForwardedHeadersOptions options, IReadOnlyList<string> trustedProxies)
    {
        // 只处理这两个头：X-Forwarded-Host / X-Forwarded-Prefix 本项目用不到，多信一个就多一个伪造面。
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);

        foreach (var entry in trustedProxies)
        {
            var text = entry.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Contains('/'))
            {
                if (!System.Net.IPNetwork.TryParse(text, out var network))
                {
                    throw new InvalidOperationException(
                        $"GameServer:TrustedProxies 里的网段不合法：'{entry}'。写成形如 172.18.0.0/16。");
                }

                options.KnownIPNetworks.Add(network);
                continue;
            }

            if (!IPAddress.TryParse(text, out var address))
            {
                throw new InvalidOperationException(
                    $"GameServer:TrustedProxies 里的地址不合法：'{entry}'。写反向代理的 IP，或形如 172.18.0.0/16 的网段。");
            }

            options.KnownProxies.Add(address);
        }
    }
}
