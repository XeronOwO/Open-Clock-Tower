using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：开局分配只能走事件流、行动契约必须带规则来源、计划不上 wire。
/// </summary>
/// <remarks>
/// 依据 D-0017（角色分配进事件流；GameSetup 只承载票据）与 AGENTS.md「规则不许凭记忆写」。
/// </remarks>
public sealed class NightBuildGateTests
{
    /// <summary>会话票据不得承载角色——角色分配的唯一载体是事件流（D-0017）。</summary>
    [Fact]
    public void SessionSetup_MustNotCarryCharacterAssignment()
    {
        string[] candidates =
        [
            RepositoryLayout.PathOf("src", "OpenClockTower.Application", "GameSetup.cs"),
            RepositoryLayout.PathOf("src", "OpenClockTower.Server", "GameSetupEntity.cs"),
        ];

        var violations = candidates
            .Where(File.Exists)
            .Where(path => SourceText
                .StripCommentsAndLiterals(File.ReadAllText(path))
                .Contains("character", StringComparison.OrdinalIgnoreCase))
            .Select(RepositoryLayout.Relative)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "会话票据不得承载角色：角色分配的唯一载体是事件流（D-0017）。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>每个实现 INightAction 的契约文件都必须引用规则来源（页名 + 抓取日期）。</summary>
    [Fact]
    public void NightActionContracts_MustCiteTheirRuleSource()
    {
        // 与门禁 1/3 同口径：只扫被 git 跟踪的文件（未提交的草稿不得制造假红）。
        var contracts = RepositoryLayout.EnumerateTrackedTextFiles()
            .Where(path => path.Replace('\\', '/').StartsWith("src/OpenClockTower.Rules/", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => Regex.IsMatch(
                File.ReadAllText(RepositoryLayout.PathOf(path)),
                @":\s*INightAction\b"))
            .ToArray();

        Assert.NotEmpty(contracts);

        var violations = contracts
            .Where(path =>
            {
                var text = File.ReadAllText(RepositoryLayout.PathOf(path));
                return !text.Contains("百科《", StringComparison.Ordinal)
                       || !text.Contains("抓取", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "行动契约必须写清规则来源（百科页名 + 抓取日期 + 区域）："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>步骤计划不进 wire 契约：客户端既不提供计划，也不该看到计划对象。</summary>
    [Fact]
    public void WireContracts_MustNotCarryStepPlans()
    {
        var files = RepositoryLayout.EnumerateSourceFiles("src", "OpenClockTower.Contracts");
        Assert.NotEmpty(files);

        var violations = files
            .Where(path =>
            {
                var text = File.ReadAllText(RepositoryLayout.PathOf(path));
                return text.Contains("StepPlan", StringComparison.Ordinal)
                       || text.Contains("StepSlot", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "计划与槽位是服务端内部对象，不得进入 wire 契约（客户端不提供计划、也不该看到计划）："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
