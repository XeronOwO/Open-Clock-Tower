namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：单个源文件不得超过 600 行。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>AGENTS.md</c>「架构硬约束」：类超过 600 行必须**先拆再改**，不是"下次再说"。
/// 因为「一文件一顶层类型」已被 <see cref="SourceShapeGateTests"/> 锁死，
/// 文件行数就是类体量的可靠代理：超限意味着这个类同时装了多份职责。
/// </para>
/// <para>
/// 这条阈值过去只写在文档里，于是 `GameSession` 一路长到 789 行才被发现——
/// 规则靠自觉守不住，所以写成会失败的测试。
/// </para>
/// </remarks>
public sealed class SourceFileLengthGateTests
{
    private const int MaxLines = 600;

    /// <summary>`src/` 与 `tests/` 下的每个 .cs 文件都必须在阈值内。</summary>
    [Fact]
    public void SourceFiles_StayUnderTheArchitectureLimit()
    {
        var files = RepositoryLayout
            .EnumerateSourceFiles("src")
            .Concat(RepositoryLayout.EnumerateSourceFiles("tests"))
            .ToList();
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var relativePath in files)
        {
            var lines = File.ReadAllLines(RepositoryLayout.PathOf(relativePath)).Length;
            if (lines > MaxLines)
            {
                violations.Add($"{relativePath} → {lines} 行");
            }
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            $"单个源文件不得超过 {MaxLines} 行（依据 AGENTS.md「架构硬约束」：超限先拆再改）。"
            + Environment.NewLine
            + "拆法：先找出这个类同时承担的职责，把其中一份搬去独立的类型，再谈加功能。"
            + Environment.NewLine
            + report);
    }
}
