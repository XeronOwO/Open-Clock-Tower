namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：操作请求里**不得出现任何超时 / 过期概念**。
/// </summary>
/// <remarks>
/// 依据 D-0011 硬约束 3：内核里不存在"过期时间"，只存在**作废原因**。
/// 这条不变量靠自觉守不住——将来一句"加个 timeout 保护"就会把无超时设计悄悄改掉，
/// 所以写成会失败的测试。
/// </remarks>
public sealed class OperationRequestNoTimeoutGateTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "Timeout",
        "Expire",
        "Expiration",
        "Deadline",
        "Ttl",
        "DateTime",
        "DateTimeOffset",
        "Stopwatch",
        "Timer",
        "Task.Delay",
    ];

    /// <summary>操作请求契约文件里不得出现超时 / 时间字段。</summary>
    [Fact]
    public void OperationRequest_DeclaresNoTimeoutConcept()
    {
        var relativePath = Path.Combine("src", "OpenClockTower.Kernel", "OperationRequest.cs");
        var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(relativePath)));

        var violations = ForbiddenTokens
            .Where(token => code.Contains(token, StringComparison.Ordinal))
            .ToList();

        var report = string.Join(", ", violations);
        Assert.True(
            violations.Count == 0,
            "操作请求里出现了超时 / 时间概念（依据 D-0011 硬约束 3：只有作废原因，没有过期时间）。"
            + Environment.NewLine
            + $"违规 token：{report}");
    }
}
