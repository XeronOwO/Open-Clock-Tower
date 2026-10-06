namespace OpenClockTower.Server;

/// <summary>
/// 真实客户端地址的**唯一读法**（M3 / G-A3-3）：日志与限速都从这里取，不许各处自己读连接端点。
/// </summary>
/// <remarks>
/// <para>
/// <c>Connection.RemoteIpAddress</c> 在管线最前面已被 <c>UseForwardedHeaders</c> 按可信代理名单改写
/// （<see cref="ForwardedHeaderPolicy"/>），所以这里拿到的既可能是反代传下来的真实地址，也可能是直连的对端。
/// </para>
/// <para>
/// 取不到地址时返回 <see cref="Unknown"/> 而不是空串：限速会把未知来源归到同一个桶里
/// ——宁可多拦一点，也不放行来源不明的尝试。
/// </para>
/// </remarks>
public static class ClientAddress
{
    /// <summary>取不到地址时的占位符（日志与限速共用一个值，方便按它检索）。</summary>
    public const string Unknown = "unknown";

    /// <summary>取真实客户端地址；拿不到就返回 <see cref="Unknown"/>。</summary>
    public static string Of(HttpContext? context) =>
        context?.Connection.RemoteIpAddress?.ToString() ?? Unknown;
}
