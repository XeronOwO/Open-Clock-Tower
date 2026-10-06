using System.Text;
using System.Text.RegularExpressions;

namespace OpenClockTower.Server;

/// <summary>
/// 响应头的统一出口（M3 / G-A3-2 · G-A3-6）：安全响应头与静态资源缓存口径都在这里发。
/// </summary>
/// <remarks>
/// <para>
/// 放在**应用**而不是反代，理由有三条：① 换任何部署方式（直接跑、别的前端代理、容器）都带着它；
/// ② 它能在运行时被测试直接证明（反代配置在本机没有可跑的判据）；③ 只有一处，
/// 不会出现"反代发一半、应用发一半"的分叉。
/// </para>
/// <para>
/// HSTS 不在本类里：它由框架的 <c>UseHsts</c> 发（只在 HTTPS 响应上加头），
/// 因为 HSTS 只对 HTTPS 有意义——明文实例上发它等于没发，浏览器按规范忽略。
/// </para>
/// <para>
/// 缓存口径：Vite 产物文件名带内容哈希（<c>/assets/index-&lt;hash&gt;.js</c>），可以长缓存 + immutable；
/// 其余（页面外壳、<c>/healthz</c>、Hub 响应）一律 <c>no-cache</c>——页面外壳被缓存住就会出现
/// "新版本已发布、浏览器还在引旧哈希资源"的白屏。
/// </para>
/// </remarks>
public sealed partial class ResponseHeadersMiddleware(RequestDelegate next)
{
    /// <summary>带内容哈希的产物目录（Vite 的 <c>assets/</c>）在应用侧的路径。</summary>
    /// <remarks>
    /// <para>子路径部署时反代会把前缀去掉，所以这里永远是根路径下的 <c>/assets</c>。</para>
    /// <para>
    /// **不带末尾斜杠**：<c>PathString.StartsWithSegments</c> 要求匹配之后紧跟一个路径分隔符，
    /// 写成 <c>/assets/</c> 反而永远匹配不上（它会在下一个字符上找 <c>/</c>）。这条踩过一次，别再改回去。
    /// </para>
    /// </remarks>
    public const string HashedAssetPrefix = "/assets";

    /// <summary>带哈希的资源可以长缓存：文件名变了 URL 就变，不会取到旧内容。</summary>
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>其余响应一律先回源校验（等价于"每次都问一句"，但走 304 不重传）。</summary>
    public const string RevalidateCacheControl = "no-cache";

    /// <summary>角色图热链的百科主机（D-0007 / R-0006）：CSP 的 <c>img-src</c> 必须放行它，否则牌面全裂。</summary>
    private const string CharacterArtOrigin = "https://clocktower-wiki.gstonegames.com";

    /// <summary>只给"用得上"的能力开口：这个站不用摄像头 / 麦克风 / 定位 / 支付 / USB。</summary>
    private const string PermissionsPolicyValue =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    /// <summary>给每个响应装上安全头与缓存口径，然后交给下游。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = PermissionsPolicyValue;
        headers["X-Frame-Options"] = "DENY";
        headers["Content-Security-Policy"] = BuildContentSecurityPolicy(context.Request);
        headers.CacheControl = CacheControlFor(context.Request.Path);

        await next(context);
    }

    /// <summary>按路径定缓存口径：带哈希的产物长缓存，其余先回源校验。</summary>
    internal static string CacheControlFor(PathString path) =>
        path.StartsWithSegments(HashedAssetPrefix, StringComparison.Ordinal)
            ? ImmutableCacheControl
            : RevalidateCacheControl;

    /// <summary>
    /// 拼本站的内容安全策略。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>connect-src</c> 除了 <c>'self'</c> 还要显式写出本站的 <c>ws://</c> / <c>wss://</c> 形态：
    /// 部分浏览器（WebKit 的
    /// <see href="https://bugs.webkit.org/show_bug.cgi?id=201591">bug 201591</see>）不把 <c>'self'</c>
    /// 解析到 WebSocket 协议上，而本项目的对局全靠 SignalR 的 WebSocket——少这一条就是"页面能开、连不上桌"。
    /// 来源只取请求自身的 Host（客户端可控），因此先按主机名字面校验，不合规就退回 <c>'self'</c>。
    /// </para>
    /// <para>
    /// 不写 <c>'unsafe-inline'</c>：前端的样式是构建期抽出的 CSS 文件，运行期只做 CSSOM 赋值
    /// （<c>element.style</c>），不受 <c>style-src</c> 的内联限制。
    /// </para>
    /// </remarks>
    private static string BuildContentSecurityPolicy(HttpRequest request)
    {
        var policy = new StringBuilder();
        policy.Append("default-src 'self'; ");
        policy.Append("base-uri 'none'; ");
        policy.Append("object-src 'none'; ");
        policy.Append("frame-ancestors 'none'; ");
        policy.Append("form-action 'self'; ");
        policy.Append("script-src 'self'; ");
        policy.Append("style-src 'self'; ");
        policy.Append("font-src 'self'; ");
        policy.Append($"img-src 'self' {CharacterArtOrigin}; ");
        policy.Append("connect-src 'self'");

        var authority = request.Host.Value;
        if (!string.IsNullOrEmpty(authority) && AuthorityValue().IsMatch(authority))
        {
            policy.Append($" ws://{authority} wss://{authority}");
        }

        policy.Append(';');
        return policy.ToString();
    }

    /// <summary>主机名字面（含端口；IPv6 允许方括号写法）：只有这种形状才允许进 CSP。</summary>
    [GeneratedRegex(
        @"^(?:[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?|\[[0-9A-Fa-f:.]+\])(?::[0-9]{1,5})?$",
        RegexOptions.None)]
    private static partial Regex AuthorityValue();
}
