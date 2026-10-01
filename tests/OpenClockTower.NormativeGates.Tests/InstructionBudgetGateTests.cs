namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 指令文件体量门禁：**仓库根之下**的每份 <c>AGENTS.md</c> 不得超过 5,120 字节。
/// </summary>
/// <remarks>
/// 依据 <c>docs/AGENTS.md</c> §6：指令文件只路由与约束，不装知识。
/// 一旦它开始装知识，就会与它指向的文档分叉，最后变成没人读的第二真相。
/// 根的 <c>AGENTS.md</c> 是长文入口，不受此限。
/// </remarks>
public sealed class InstructionBudgetGateTests
{
    private const int BudgetBytes = 5 * 1024;

    [Fact]
    public void InstructionFilesBelowRepositoryRoot_StayWithinBudget()
    {
        var rootAgents = RepositoryLayout.PathOf("AGENTS.md");
        var files = Directory.EnumerateFiles(RepositoryLayout.Root, "AGENTS.md", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, rootAgents, StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(path))
            .ToList();

        Assert.NotEmpty(files);

        var violations = files
            .Select(path => new FileInfo(path))
            .Where(info => info.Length > BudgetBytes)
            .Select(info => $"{RepositoryLayout.Relative(info.FullName)} → {info.Length} 字节（上限 {BudgetBytes}）")
            .ToList();

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "指令文件超出体量上限（依据 docs/AGENTS.md §6）。" + Environment.NewLine
            + "把细节移进它链接的文档，这里只留路由与约束。" + Environment.NewLine
            + report);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
