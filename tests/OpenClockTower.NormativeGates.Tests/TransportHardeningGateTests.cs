namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 反代模板的加固项门禁（M3 / G-A3-4 · G-A5-7）：这份模板是陌生人部署时唯一会照抄的配置，
/// 少一条"上限"或"超时"，对方不会知道少了什么——所以把它写成会失败的测试。
/// </summary>
/// <remarks>
/// 判的是**文本存在性**，不判 nginx 语法（那要一台跑着 nginx 的机器，属真机读数，见
/// <c>tools/deploy/templates</c> 与部署文档）。存在性门禁的价值在于：改模板的人删掉某条时，
/// 必须在测试里显式删掉这一行——那时他至少会看见"这是有意为之的"。
/// </remarks>
public sealed class TransportHardeningGateTests
{
    private static readonly string[] RequiredDirectives =
    [
        // 不白送指纹
        "server_tokens off;",
        // 三层上限：请求速率、并发连接、请求体大小
        "limit_req_zone",
        "limit_conn_zone",
        "limit_req zone=",
        "limit_conn clocktower_conn",
        "client_max_body_size",
        // 慢连接超时
        "client_header_timeout",
        "client_body_timeout",
        "send_timeout",
        // 与应用侧同源的说明（配置分叉时能顺着这句话找到另一半）
        "GameServer__Transport",
    ];

    private static string Template =>
        File.ReadAllText(RepositoryLayout.PathOf("tools", "deploy", "templates", "clocktower.conf.template"));

    [Fact]
    public void NginxTemplate_CarriesExplicitLimitsAndTimeouts()
    {
        var template = Template;
        var missing = RequiredDirectives
            .Where(directive => !template.Contains(directive, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "反代模板少了加固项（M3 / G-A3-4：上限与超时都要显式，不吃默认值）：" + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void NginxTemplate_DoesNotDuplicateTheApplicationSecurityHeaders()
    {
        // 安全响应头只有应用这一个出口（ResponseHeadersMiddleware）。两处都写必然漂移，
        // 而且出问题时看不出"少了哪一个、是谁少的"。
        Assert.DoesNotContain("add_header", Template, StringComparison.Ordinal);
    }

    [Fact]
    public void NginxTemplate_KeepsTheLongLivedTimeoutOnTheHubLocationOnly()
    {
        // 对局中的 WebSocket 需要一小时级的读超时，页面与静态资源不需要——
        // 原来整站都是 3600s，等于让慢连接能挂一小时（审计 G-A3-4 的原话）。
        var hubBlock = BlockOf("{{LOCATION}}hub/");
        var siteBlock = BlockOf("location {{LOCATION}} {");

        Assert.Contains("proxy_read_timeout 3600s;", hubBlock, StringComparison.Ordinal);
        Assert.Contains("proxy_read_timeout 60s;", siteBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("proxy_read_timeout 3600s;", siteBlock, StringComparison.Ordinal);
    }

    /// <summary>取模板里以某个片段开头的那一段配置（到该块结束的括号为止）。</summary>
    private static string BlockOf(string marker)
    {
        var template = Template;
        var start = template.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"模板里找不到 {marker} —— 结构变了就要同步改这条门禁。");

        var end = template.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, $"模板里 {marker} 这一块没有正常结束。");

        return template[start..end];
    }
}
