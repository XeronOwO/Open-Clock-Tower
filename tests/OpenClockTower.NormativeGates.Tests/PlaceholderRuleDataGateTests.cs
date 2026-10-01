namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：生产代码不得再内置演示 / 占位步骤表。
/// </summary>
/// <remarks>
/// <para>
/// 依据 docs/architecture/current.md §2.6：夜晚的步骤序列只能由结算引擎按剧本的完整夜晚顺序表构建
/// （顺序表数据在 <c>OpenClockTower.Rules</c>）。生产代码里放一份"演示计划"看上去能让链路跑起来，
/// 但它会被当成规则——这正是本项目的头号事故源。
/// </para>
/// <para>
/// 曾经的 <c>DemoStepPlan</c> 已从 <c>src/</c> 移除（集成测试改用 <c>tests/</c> 里的测试夹具）。
/// 匹配**不区分大小写**并覆盖已知命名变体（<c>demostepplan</c> / <c>demoplan</c>），
/// 避免"改个大小写或换个后缀就绕过"。注释里的提及同样拦截——`src/` 里不该出现这类名字。
/// </para>
/// </remarks>
public sealed class PlaceholderRuleDataGateTests
{
    /// <summary>被禁止的占位命名（已转小写比较）。</summary>
    private static readonly string[] ForbiddenTokens = ["demostepplan", "demoplan"];

    [Fact]
    public void ProductionCode_ContainsNoDemoPlanPlaceholder()
    {
        var files = RepositoryLayout.EnumerateSourceFiles("src");
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var path in files)
        {
            var code = File.ReadAllText(RepositoryLayout.PathOf(path)).ToLowerInvariant();
            violations.AddRange(
                ForbiddenTokens
                    .Where(token => code.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{path} → {token}"));
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "生产代码不得内置演示 / 占位计划（依据 docs/architecture/current.md §2.6："
            + "夜晚计划由结算引擎按 Rules 顺序表构建，占位会被误当规则）。"
            + Environment.NewLine
            + report);
    }
}
