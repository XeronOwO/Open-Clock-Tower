namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 内核纯净门禁：<c>src/OpenClockTower.Kernel</c> 里不得出现非确定性或 IO 调用。
/// </summary>
/// <remarks>
/// 依据 <c>docs/decisions/active.md</c> D-0008：内核必须是纯计算，
/// 给定（状态 + 说书人裁定输入）必产出同一结果。一旦内核里出现时间、随机、IO 或
/// 字典枚举顺序依赖，回放就会漂移、撤销就不可信、断线重连就会分叉。
/// 这条不变量靠自觉守不住，所以写成会失败的测试。
/// </remarks>
public sealed class KernelPurityGateTests
{
    private static readonly string[] ForbiddenTokens =
    [
        // 时间
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTime.Today",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
        "Environment.TickCount",
        "Stopwatch",

        // 随机与身份
        "Random.Shared",
        "new Random(",
        "System.Random",
        "Guid.NewGuid",

        // 并发等待
        "Task.Delay",
        "Thread.Sleep",

        // IO 与网络
        "System.IO",
        "FileStream",
        "StreamReader",
        "StreamWriter",
        "System.Net",
        "HttpClient",

        // 控制台与进程
        "Console.Write",
        "Console.ReadLine",
        "Environment.Exit",
    ];

    [Fact]
    public void Kernel_ContainsNoNondeterministicOrIoApis()
    {
        var files = RepositoryLayout.EnumerateSourceFiles("src", "OpenClockTower.Kernel");
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var relativePath in files)
        {
            var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(relativePath)));
            violations.AddRange(
                ForbiddenTokens
                    .Where(token => code.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{relativePath} → {token}"));
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "内核里出现了非确定性或 IO 调用（依据 D-0008，内核必须保持纯净）。" + Environment.NewLine
            + "若确实需要这些能力，把它抽成接口由上层注入，而不是在内核里直接调用。" + Environment.NewLine
            + report);
    }
}
