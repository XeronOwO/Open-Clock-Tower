namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 依赖方向门禁：下层不得引用上层。
/// </summary>
/// <remarks>
/// 依据 <c>docs/architecture/current.md</c> §1 的分层：依赖只能从上往下。
/// 一旦 <c>Kernel</c> 引用了 <c>Rules</c> 或 <c>Application</c>，依赖图成环，
/// 内核就再也无法被单独测试，纯净门禁也会随之失效（它只能扫到被引用进来的代码）。
/// </remarks>
public sealed class DependencyDirectionGateTests
{
    /// <summary>声明的层级与它**不允许**引用的项目。</summary>
    private static readonly LayerRule[] Rules =
    [
        new(
            "src/OpenClockTower.Kernel",
            ["OpenClockTower.Rules", "OpenClockTower.Application", "OpenClockTower.Contracts", "OpenClockTower.Server"]),
        new(
            "src/OpenClockTower.Rules",
            ["OpenClockTower.Application", "OpenClockTower.Contracts", "OpenClockTower.Server"]),
        new(
            "src/OpenClockTower.Contracts",
            ["OpenClockTower.Kernel", "OpenClockTower.Rules", "OpenClockTower.Application", "OpenClockTower.Server"]),
    ];

    [Fact]
    public void LayerProjects_DoNotReferenceUpward()
    {
        var violations = new List<string>();
        foreach (var rule in Rules)
        {
            var projectDirectory = RepositoryLayout.PathOf(rule.ProjectPath);
            var projectFiles = Directory.EnumerateFiles(projectDirectory, "*.csproj").ToList();
            Assert.Single(projectFiles);

            var projectText = File.ReadAllText(projectFiles[0]);
            violations.AddRange(
                rule.ForbiddenReferences
                    .Where(forbidden => projectText.Contains($"{forbidden}.csproj", StringComparison.Ordinal))
                    .Select(forbidden => $"{rule.ProjectPath} → {forbidden}"));
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "依赖方向被打破（依据 docs/architecture/current.md §1）。" + Environment.NewLine
            + "正确的方向是 Server → Application → Rules → Kernel，Contracts 独立。" + Environment.NewLine
            + report);
    }

    private sealed record LayerRule(string ProjectPath, string[] ForbiddenReferences);
}
