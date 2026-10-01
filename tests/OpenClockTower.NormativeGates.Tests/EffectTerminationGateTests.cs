using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：持续型效果的终止**必须带原因**——内核里不允许出现无参的 <c>Terminate()</c>。
/// </summary>
/// <remarks>
/// <para>
/// 依据票据「说书人上帝视角」第 2 条，以及百科《重要细节》二-3 / 二-7（见 `rulings.md` R-0012）：
/// 持续型效果会随来源死亡或换角色而终止，这句话正是"投毒者死了，所以他下的毒解了"要被回答的地方。
/// "效果没了"却不写明为什么，等于把上帝视角最需要的那一步留白。
/// </para>
/// <para>
/// 这条不变量靠自觉守不住，所以写成会失败的测试：谁再写一个无参终止，
/// 门禁就在提交前红给他看（含 <c>Terminate( )</c>、跨行括号等写法），而不是等复盘时才发现账里缺了半句话。
/// </para>
/// </remarks>
public sealed partial class EffectTerminationGateTests
{
    /// <summary>匹配无参终止的声明或调用：<c>Terminate()</c> / <c>Terminate( )</c> / <c>Terminate(\n)</c>。</summary>
    [GeneratedRegex(@"Terminate\s*\(\s*\)")]
    private static partial Regex ParameterlessTerminate();

    /// <summary>内核任何位置都不得出现无参终止。</summary>
    [Fact]
    public void Kernel_NeverTerminatesAnEffectWithoutAReason()
    {
        var files = RepositoryLayout.EnumerateSourceFiles("src", "OpenClockTower.Kernel");
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var relativePath in files)
        {
            var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(relativePath)));
            if (ParameterlessTerminate().IsMatch(code))
            {
                violations.Add($"{relativePath} → Terminate()");
            }
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "出现了不带原因的终止（依据票据「说书人上帝视角」第 2 条：终止必须可归因）。"
            + Environment.NewLine
            + "改用 Terminate(EffectTermination)，把原因分类与说明一起记进事件流。"
            + Environment.NewLine
            + report);
    }
}
